using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OdooPosLogin;

public record ConnectionTestResult(bool Success, string Message, int? Uid = null);

public record SyncProgressReport(
    int StepNumber,
    int TotalSteps,
    string CurrentModule,
    string StatusMessage,
    double Percentage
);

public record SyncAllSummary(
    bool Success,
    string Message,
    int CompaniesCount,
    int UsersCount,
    int PaymentMethodsCount,
    int CategoriesCount,
    int AttributesCount,
    int TagsCount,
    int CombosCount,
    int ProductsCount,
    int PosConfigsCount,
    int SessionsCount,
    int OrdersCount,
    int PaymentsCount
);

public record OdooCompanyDto(
    int OdooId,
    string Name,
    string Street,
    string Street2,
    string City,
    string StateName,
    string Zip,
    string CountryName,
    string Vat,
    string Ice,
    string CurrencyName,
    string Phone,
    string Email,
    string Website,
    string EmailDomain,
    int ColorIndex,
    string ColorHex,
    int? ParentId,
    string ParentName,
    string? LogoBase64
);

public record PosPaymentMethodDto(
    int OdooId,
    string Name,
    string MethodType,
    int? JournalId,
    string JournalName,
    int? CompanyId,
    string CompanyName,
    string LinkedPosConfigs,
    string Integration,
    string? ImageBase64
);

public class OdooSyncService
{
    private static readonly Lazy<OdooSyncService> _instance = new(() => new OdooSyncService());
    public static OdooSyncService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, HashSet<string>> _modelFieldsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private int _rpcIdCounter = 100;

    public bool IsOnline { get; private set; }
    public string ConnectionStatusLabel { get; private set; } = "Offline (Local Mode)";
    public event Action<bool>? OnlineStatusChanged;
    public event Action<int>? PendingQueueFlushed;

    private OdooSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "http://localhost:8069";
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }
        return url.TrimEnd('/');
    }

    public async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                UpdateStatus(false, "Offline (Local Mode)");
                return false;
            }

            bool reachable = await CheckServerReachableAsync(config.ServerUrl);
            UpdateStatus(reachable, reachable ? "Odoo Online" : "Offline (Local Mode)");

            if (reachable)
            {
                // Automatically flush any queued offline transactions in sequential order
                _ = Task.Run(() => ProcessPendingQueueAsync());
            }

            return reachable;
        }
        catch
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return false;
        }
    }

    public void UpdateStatus(bool online, string label)
    {
        bool changed = (IsOnline != online) || (ConnectionStatusLabel != label);
        IsOnline = online;
        ConnectionStatusLabel = label;
        if (changed)
        {
            try
            {
                OnlineStatusChanged?.Invoke(online);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnlineStatusChanged error] {ex}");
            }
        }
    }

    public async Task<bool> CheckServerReachableAsync(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;

        string baseUrl = NormalizeUrl(serverUrl);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var resp = await _httpClient.GetAsync($"{baseUrl}/web/login", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500;
        }
        catch
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var resp2 = await _httpClient.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                return (int)resp2.StatusCode >= 200 && (int)resp2.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(OdooConfigRecord config)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, "Server URL is required.");
        }
        if (string.IsNullOrWhiteSpace(config.DatabaseName))
            return new ConnectionTestResult(false, "Database name is required.");
        if (string.IsNullOrWhiteSpace(config.Username))
            return new ConnectionTestResult(false, "Username / Email is required.");

        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        try
        {
            var authPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "common",
                    method = "authenticate",
                    args = new object[]
                    {
                        config.DatabaseName.Trim(),
                        config.Username.Trim(),
                        config.ApiKey ?? "",
                        new { }
                    }
                },
                id = Interlocked.Increment(ref _rpcIdCounter)
            };

            using var jsonContent = new StringContent(
                JsonSerializer.Serialize(authPayload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(endpoint, jsonContent);
            if (response.IsSuccessStatusCode)
            {
                string respString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respString);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var resultProp))
                {
                    if (resultProp.ValueKind == JsonValueKind.Number && resultProp.TryGetInt32(out int uid) && uid > 0)
                    {
                        UpdateStatus(true, "Odoo Online");
                        return new ConnectionTestResult(true, $"Connected (UID: {uid})", uid);
                    }
                    if (resultProp.ValueKind == JsonValueKind.False)
                    {
                        UpdateStatus(true, "Odoo Online");
                        return new ConnectionTestResult(false, "Authentication Failed: Invalid Database, Username, or Password/API Key.");
                    }
                }

                if (root.TryGetProperty("error", out var errorProp))
                {
                    UpdateStatus(true, "Odoo Online");
                    return new ConnectionTestResult(false, $"Odoo Error: {ExtractRpcErrorMessage(errorProp)}");
                }
            }

            UpdateStatus(true, "Odoo Online");
            return new ConnectionTestResult(false, $"Server returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, $"Cannot reach Odoo server ({baseUrl}): {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, "Connection timed out. Verify the Odoo server URL and network.");
        }
        catch (Exception ex)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, $"Error: {ex.Message}");
        }
    }

    // =========================================================================
    // LOW-LEVEL JSON-RPC HELPERS
    // =========================================================================
    private async Task<int> EnsureAuthenticatedUidAsync(OdooConfigRecord config)
    {
        var test = await TestConnectionAsync(config);
        if (!test.Success || !test.Uid.HasValue)
        {
            throw new InvalidOperationException(test.Message);
        }
        return test.Uid.Value;
    }

    private async Task<JsonDocument> ExecuteKwAsync(
        OdooConfigRecord config,
        int uid,
        string model,
        string method,
        object[] args,
        object? kwargs = null)
    {
        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        object[] rpcArgs = kwargs != null
            ? [config.DatabaseName.Trim(), uid, config.ApiKey ?? "", model, method, args, kwargs]
            : [config.DatabaseName.Trim(), uid, config.ApiKey ?? "", model, method, args];

        var payload = new
        {
            jsonrpc = "2.0",
            method = "call",
            @params = new
            {
                service = "object",
                method = "execute_kw",
                args = rpcArgs
            },
            id = Interlocked.Increment(ref _rpcIdCounter)
        };

        using var reqContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, reqContent);
        response.EnsureSuccessStatusCode();

        string responseStr = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseStr);

        if (doc.RootElement.TryGetProperty("error", out var errorProp))
        {
            string msg = ExtractRpcErrorMessage(errorProp);
            doc.Dispose();
            throw new InvalidOperationException($"Odoo RPC error on {model}.{method}: {msg}");
        }

        UpdateStatus(true, "Odoo Online");
        return doc;
    }

    private Task<JsonDocument> SearchReadAsync(
        OdooConfigRecord config,
        int uid,
        string model,
        object[] domain,
        string[] fields)
    {
        return ExecuteKwAsync(
            config,
            uid,
            model,
            "search_read",
            [domain],
            new
            {
                fields,
                context = new { bin_size = false }
            }
        );
    }

    private async Task<HashSet<string>> GetAvailableFieldsAsync(OdooConfigRecord config, int uid, string model)
    {
        string cacheKey = $"{NormalizeUrl(config.ServerUrl)}|{config.DatabaseName}|{model}";
        if (_modelFieldsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var doc = await ExecuteKwAsync(
                config,
                uid,
                model,
                "fields_get",
                [],
                new { attributes = new[] { "type" } }
            );

            var fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in res.EnumerateObject())
                {
                    fieldSet.Add(prop.Name);
                }
            }

            if (fieldSet.Count > 0)
            {
                _modelFieldsCache[cacheKey] = fieldSet;
            }
            return fieldSet;
        }
        catch
        {
            return [];
        }
    }

    private async Task<string[]> FilterValidFieldsAsync(OdooConfigRecord config, int uid, string model, params string[] desiredFields)
    {
        var available = await GetAvailableFieldsAsync(config, uid, model);
        if (available.Count == 0)
        {
            return desiredFields;
        }
        return desiredFields.Where(f => available.Contains(f)).ToArray();
    }

    private static string ExtractRpcErrorMessage(JsonElement errorProp)
    {
        if (errorProp.TryGetProperty("data", out var dataProp))
        {
            if (dataProp.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                return msgProp.GetString() ?? "Odoo RPC Error";
            if (dataProp.TryGetProperty("debug", out var dbgProp) && dbgProp.ValueKind == JsonValueKind.String)
                return dbgProp.GetString() ?? "Odoo RPC Error";
        }
        if (errorProp.TryGetProperty("message", out var mProp) && mProp.ValueKind == JsonValueKind.String)
        {
            return mProp.GetString() ?? "Odoo RPC Error";
        }
        return "Unknown Odoo RPC Error";
    }

    private static string GetStringOrDefault(JsonElement item, string propName, string defaultValue = "")
    {
        if (item.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            string? val = prop.GetString();
            if (!string.IsNullOrWhiteSpace(val) && !val.Equals("false", StringComparison.OrdinalIgnoreCase))
                return val;
        }
        return defaultValue;
    }

    private static string? GetNullableString(JsonElement item, params string[] propNames)
    {
        foreach (string name in propNames)
        {
            if (!item.TryGetProperty(name, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.String)
            {
                string? s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            else if (prop.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in new[] { "content", "data", "value", "base64" })
                {
                    if (prop.TryGetProperty(key, out var sub) && sub.ValueKind == JsonValueKind.String)
                    {
                        string? s = sub.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase))
                            return s;
                    }
                }

                foreach (var child in prop.EnumerateObject())
                {
                    if (child.Value.ValueKind == JsonValueKind.String &&
                        !child.Name.Equals("filename", StringComparison.OrdinalIgnoreCase) &&
                        !child.Name.Equals("mimetype", StringComparison.OrdinalIgnoreCase))
                    {
                        string? s = child.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && s.Length > 32)
                            return s;
                    }
                }
            }
        }
        return null;
    }

    private static int GetIntOrDefault(JsonElement item, string propName, int defaultValue = 0)
    {
        if (item.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int v))
            return v;
        return defaultValue;
    }

    private static decimal GetDecimalOrDefault(JsonElement item, string propName, decimal defaultValue = 0.00m)
    {
        if (item.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            if (prop.TryGetDecimal(out decimal d)) return d;
            if (prop.TryGetDouble(out double db)) return Convert.ToDecimal(db);
        }
        return defaultValue;
    }

    private static bool GetBoolOrDefault(JsonElement item, string propName, bool defaultValue = false)
    {
        if (item.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int i)) return i != 0;
        }
        return defaultValue;
    }

    private static (int? id, string name) GetMany2One(JsonElement item, string propName)
    {
        if (item.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() >= 2)
            {
                int? id = prop[0].ValueKind == JsonValueKind.Number ? prop[0].GetInt32() : null;
                string name = prop[1].ValueKind == JsonValueKind.String ? prop[1].GetString() ?? "" : "";
                return (id, name);
            }
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int idOnly))
            {
                return (idOnly, "");
            }
        }
        return (null, "");
    }

    private static List<int> GetIdArray(JsonElement item, string propName)
    {
        var list = new List<int>();
        if (item.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int id))
                    list.Add(id);
            }
        }
        return list;
    }

    private static DateTime? GetDateTimeOrNull(JsonElement item, string propName)
    {
        string raw = GetStringOrDefault(item, propName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt.ToLocalTime();
        if (DateTime.TryParse(raw, out var localDt))
            return localDt;
        return null;
    }

    // =========================================================================
    // SEQUENTIAL FIFO OFFLINE SYNC QUEUE PROCESSOR (TWO-WAY POS SYNC)
    // =========================================================================
    public async Task<int> ProcessPendingQueueAsync(OdooConfigRecord? overrideConfig = null)
    {
        if (!await _queueLock.WaitAsync(0))
            return 0;

        int processedCount = 0;
        try
        {
            var queueItems = await SqliteDatabaseService.Instance.GetPendingSyncQueueAsync();
            if (queueItems.Count == 0)
                return 0;

            var config = overrideConfig ?? await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) ||
                string.IsNullOrWhiteSpace(config.DatabaseName) ||
                string.IsNullOrWhiteSpace(config.Username))
            {
                return 0;
            }

            if (!await CheckServerReachableAsync(config.ServerUrl))
            {
                UpdateStatus(false, "Offline (Local Mode)");
                return 0;
            }

            int uid = await EnsureAuthenticatedUidAsync(config);

            foreach (var item in queueItems)
            {
                try
                {
                    switch (item.OperationType)
                    {
                        case "OPEN_SESSION":
                        {
                            var payload = JsonSerializer.Deserialize<OpenSessionSyncPayload>(item.PayloadJson);
                            if (payload != null)
                            {
                                await PushOpenSessionToOdooAsync(config, uid, payload);
                            }
                            break;
                        }
                        case "CREATE_ORDER":
                        {
                            var payload = JsonSerializer.Deserialize<CreateOrderSyncPayload>(item.PayloadJson);
                            if (payload != null)
                            {
                                int? effectiveSessionOdooId = item.SessionOdooId ?? payload.SessionOdooId;
                                await PushPosOrderToOdooAsync(config, uid, payload, effectiveSessionOdooId);
                            }
                            break;
                        }
                        case "CASH_IN_OUT":
                        {
                            var payload = JsonSerializer.Deserialize<CashInOutSyncPayload>(item.PayloadJson);
                            if (payload != null)
                            {
                                int? effectiveSessionOdooId = item.SessionOdooId ?? payload.SessionOdooId;
                                await PushCashInOutToOdooAsync(config, uid, payload, effectiveSessionOdooId);
                            }
                            break;
                        }
                        case "CLOSE_SESSION":
                        {
                            var payload = JsonSerializer.Deserialize<CloseSessionSyncPayload>(item.PayloadJson);
                            if (payload != null)
                            {
                                int? effectiveSessionOdooId = item.SessionOdooId ?? payload.SessionOdooId;
                                await PushCloseSessionToOdooAsync(config, uid, payload, effectiveSessionOdooId);
                            }
                            break;
                        }
                    }

                    await SqliteDatabaseService.Instance.MarkSyncQueueItemCompletedAsync(item.Id);
                    processedCount++;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Network dropped mid-queue: stop immediately to preserve strict sequential order
                    UpdateStatus(false, "Offline (Local Mode)");
                    await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(item.Id, ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(item.Id, ex.Message);
                    // If opening a session failed, do not attempt dependent orders for that session yet
                    if (item.OperationType == "OPEN_SESSION")
                        break;
                }
            }

            if (processedCount > 0)
            {
                try
                {
                    PendingQueueFlushed?.Invoke(processedCount);
                }
                catch { }
            }
        }
        catch
        {
            // Ignore top-level background queue errors
        }
        finally
        {
            _queueLock.Release();
        }

        return processedCount;
    }

    private async Task<int> EnsureOdooOpenSessionIdAsync(OdooConfigRecord config, int uid, int configOdooId, int? knownSessionOdooId = null)
    {
        // 1. Check if knownSessionOdooId is still valid and not closed
        if (knownSessionOdooId.HasValue && knownSessionOdooId.Value > 0)
        {
            using var checkDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", knownSessionOdooId.Value }],
                ["id", "name", "state"]
            );
            if (checkDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                string state = GetStringOrDefault(first, "state", "opened");
                if (!string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase))
                {
                    return knownSessionOdooId.Value;
                }
            }
        }

        // 2. Search for any non-closed session on Odoo for this pos.config
        using (var searchDoc = await SearchReadAsync(
            config,
            uid,
            "pos.session",
            [
                new object[] { "config_id", "=", configOdooId },
                new object[] { "state", "!=", "closed" }
            ],
            ["id", "name", "state"]
        ))
        {
            if (searchDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                return GetIntOrDefault(arr[0], "id");
            }
        }

        // 3. Create a new pos.session on Odoo for this pos.config
        using var createDoc = await ExecuteKwAsync(
            config,
            uid,
            "pos.session",
            "create",
            [
                new Dictionary<string, object>
                {
                    ["config_id"] = configOdooId,
                    ["user_id"] = uid
                }
            ]
        );

        if (createDoc.RootElement.TryGetProperty("result", out var res))
        {
            if (res.ValueKind == JsonValueKind.Number && res.TryGetInt32(out int newId) && newId > 0)
                return newId;
            if (res.ValueKind == JsonValueKind.Array && res.GetArrayLength() > 0 && res[0].TryGetInt32(out int arrId))
                return arrId;
        }

        throw new InvalidOperationException($"Could not open or create a POS session in Odoo for config ID {configOdooId}.");
    }

    private async Task PushOpenSessionToOdooAsync(OdooConfigRecord config, int uid, OpenSessionSyncPayload payload)
    {
        int odooSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, payload.ExistingSessionOdooId);

        // Call set_opening_control on pos.session (sets cash_register_balance_start, opening_notes, and opens session)
        try
        {
            using var _ = await ExecuteKwAsync(
                config,
                uid,
                "pos.session",
                "set_opening_control",
                [
                    new[] { odooSessionId },
                    (double)payload.OpeningBalance,
                    payload.OpeningNote ?? ""
                ]
            );
        }
        catch
        {
            // Fallback if set_opening_control signature differs or session is already opened
            try
            {
                using var _ = await ExecuteKwAsync(
                    config,
                    uid,
                    "pos.session",
                    "action_pos_session_open",
                    [new[] { odooSessionId }]
                );
            }
            catch { }
        }

        // Read back the session name from Odoo
        string? odooSessionName = null;
        try
        {
            using var sessDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", odooSessionId }],
                ["id", "name", "state"]
            );
            if (sessDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                odooSessionName = GetStringOrDefault(arr[0], "name");
            }
        }
        catch { }

        await SqliteDatabaseService.Instance.BindLocalSessionToOdooSessionAsync(
            payload.LocalSessionId,
            payload.ConfigOdooId,
            odooSessionId,
            odooSessionName
        );
    }

    private async Task<Dictionary<int, int>> ResolveProductVariantIdsAsync(OdooConfigRecord config, int uid, IEnumerable<int> templateIds)
    {
        var map = new Dictionary<int, int>();
        var distinctIds = templateIds.Where(id => id > 0).Distinct().ToArray();
        if (distinctIds.Length == 0) return map;

        try
        {
            using var doc = await SearchReadAsync(
                config,
                uid,
                "product.product",
                [new object[] { "product_tmpl_id", "in", distinctIds }],
                ["id", "product_tmpl_id"]
            );

            if (doc.RootElement.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    int prodVariantId = GetIntOrDefault(el, "id");
                    var (tmplId, _) = GetMany2One(el, "product_tmpl_id");
                    if (prodVariantId > 0 && tmplId.HasValue && !map.ContainsKey(tmplId.Value))
                    {
                        map[tmplId.Value] = prodVariantId;
                    }
                }
            }
        }
        catch { }

        return map;
    }

    private async Task PushPosOrderToOdooAsync(
        OdooConfigRecord config,
        int uid,
        CreateOrderSyncPayload payload,
        int? sessionOdooId)
    {
        // 1. Idempotency check: see if an order with this pos_reference already exists in Odoo
        if (!string.IsNullOrWhiteSpace(payload.PosReference))
        {
            using var existingDoc = await SearchReadAsync(
                config,
                uid,
                "pos.order",
                [new object[] { "pos_reference", "=", payload.PosReference }],
                ["id", "name"]
            );

            if (existingDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                int existingOdooId = GetIntOrDefault(arr[0], "id");
                string existingName = GetStringOrDefault(arr[0], "name", payload.Name);
                if (existingOdooId > 0)
                {
                    await SqliteDatabaseService.Instance.BindLocalOrderToOdooOrderAsync(
                        payload.LocalOrderId,
                        payload.OrderUuid,
                        existingOdooId,
                        existingName
                    );
                    return;
                }
            }
        }

        // 2. Ensure we have an active Odoo session ID
        int activeSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, sessionOdooId);

        // 3. Resolve product.template IDs -> product.product variant IDs
        var tmplIds = payload.Lines
            .Where(l => l.ProductOdooId.HasValue)
            .Select(l => l.ProductOdooId!.Value);
        var variantMap = await ResolveProductVariantIdsAsync(config, uid, tmplIds);

        // Fallback product ID if a custom/unmapped product was used
        int fallbackProductId = variantMap.Values.FirstOrDefault();
        if (fallbackProductId == 0)
        {
            using var anyProdDoc = await SearchReadAsync(
                config,
                uid,
                "product.product",
                [new object[] { "available_in_pos", "=", true }],
                ["id"]
            );
            if (anyProdDoc.RootElement.TryGetProperty("result", out var pArr) &&
                pArr.ValueKind == JsonValueKind.Array &&
                pArr.GetArrayLength() > 0)
            {
                fallbackProductId = GetIntOrDefault(pArr[0], "id");
            }
        }

        // 4. Build pos.order.line tuples: [0, 0, { ... }]
        var orderLineFields = await GetAvailableFieldsAsync(config, uid, "pos.order.line");
        var lineTuples = new List<object>();

        foreach (var l in payload.Lines)
        {
            int prodId = (l.ProductOdooId.HasValue && variantMap.TryGetValue(l.ProductOdooId.Value, out int vId))
                ? vId
                : (l.ProductOdooId ?? fallbackProductId);

            var lineDict = new Dictionary<string, object>
            {
                ["product_id"] = prodId,
                ["qty"] = (double)l.Qty,
                ["price_unit"] = (double)l.PriceUnit,
                ["discount"] = (double)l.Discount,
                ["price_subtotal"] = (double)l.PriceSubtotal,
                ["price_subtotal_incl"] = (double)l.PriceSubtotalIncl
            };

            if (orderLineFields.Count == 0 || orderLineFields.Contains("full_product_name"))
                lineDict["full_product_name"] = l.ProductName;
            if (orderLineFields.Count == 0 || orderLineFields.Contains("name"))
                lineDict["name"] = l.ProductName;
            if (!string.IsNullOrWhiteSpace(l.CustomerNote) && (orderLineFields.Count == 0 || orderLineFields.Contains("customer_note")))
                lineDict["customer_note"] = l.CustomerNote;

            lineTuples.Add(new object[] { 0, 0, lineDict });
        }

        // 5. Resolve payment method ID on Odoo
        var paymentMethods = await SqliteDatabaseService.Instance.LoadAllPaymentMethodsAsync();
        var orderFields = await GetAvailableFieldsAsync(config, uid, "pos.order");

        var orderVals = new Dictionary<string, object>
        {
            ["session_id"] = activeSessionId,
            ["pos_reference"] = payload.PosReference,
            ["amount_total"] = (double)payload.AmountTotal,
            ["amount_tax"] = (double)payload.AmountTax,
            ["amount_paid"] = (double)payload.AmountPaid,
            ["amount_return"] = 0.0,
            ["lines"] = lineTuples
        };

        if (orderFields.Count == 0 || orderFields.Contains("user_id"))
            orderVals["user_id"] = payload.CashierId ?? uid;
        if (orderFields.Count == 0 || orderFields.Contains("date_order"))
            orderVals["date_order"] = payload.DateOrder.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(payload.TrackingNumber) && (orderFields.Count == 0 || orderFields.Contains("tracking_number")))
            orderVals["tracking_number"] = payload.TrackingNumber;
        if (!string.IsNullOrWhiteSpace(payload.CashierName) && (orderFields.Count == 0 || orderFields.Contains("cashier")))
            orderVals["cashier"] = payload.CashierName;
        if (payload.PartnerId.HasValue && payload.PartnerId.Value > 0 && (orderFields.Count == 0 || orderFields.Contains("partner_id")))
            orderVals["partner_id"] = payload.PartnerId.Value;
        if (payload.IsToInvoice && (orderFields.Count == 0 || orderFields.Contains("to_invoice")))
            orderVals["to_invoice"] = true;
        if (!string.IsNullOrWhiteSpace(payload.Note))
        {
            if (orderFields.Contains("general_note"))
                orderVals["general_note"] = payload.Note;
            else if (orderFields.Contains("note"))
                orderVals["note"] = payload.Note;
        }
        if (orderFields.Contains("uuid"))
            orderVals["uuid"] = payload.OrderUuid;

        int createdOrderId = 0;
        using (var createOrderDoc = await ExecuteKwAsync(config, uid, "pos.order", "create", [orderVals]))
        {
            if (createOrderDoc.RootElement.TryGetProperty("result", out var res))
            {
                if (res.ValueKind == JsonValueKind.Number && res.TryGetInt32(out int oid))
                    createdOrderId = oid;
                else if (res.ValueKind == JsonValueKind.Array && res.GetArrayLength() > 0 && res[0].TryGetInt32(out int aoid))
                    createdOrderId = aoid;
            }
        }

        if (createdOrderId <= 0)
            throw new InvalidOperationException("Failed to create pos.order in Odoo.");

        // 6. Create payments in Odoo and mark the order as Paid
        foreach (var pay in payload.Payments)
        {
            int? methodOdooId = pay.PaymentMethodOdooId;
            if (!methodOdooId.HasValue || methodOdooId.Value <= 0)
            {
                var matchedMethod = paymentMethods.FirstOrDefault(m =>
                    string.Equals(m.Name, pay.PaymentMethodName, StringComparison.OrdinalIgnoreCase));
                methodOdooId = matchedMethod?.OdooId;
            }

            if (!methodOdooId.HasValue || methodOdooId.Value <= 0)
            {
                methodOdooId = paymentMethods.FirstOrDefault()?.OdooId ?? 1;
            }

            try
            {
                var payVals = new Dictionary<string, object>
                {
                    ["pos_order_id"] = createdOrderId,
                    ["session_id"] = activeSessionId,
                    ["payment_method_id"] = methodOdooId.Value,
                    ["amount"] = (double)pay.Amount,
                    ["payment_date"] = pay.PaymentDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                };
                using var _ = await ExecuteKwAsync(config, uid, "pos.payment", "create", [payVals]);
            }
            catch { }
        }

        try
        {
            using var _ = await ExecuteKwAsync(config, uid, "pos.order", "action_pos_order_paid", [new[] { createdOrderId }]);
        }
        catch { }

        // Read back the Odoo-generated order name (e.g. "Bakery Shop/0001")
        string? odooOrderName = null;
        try
        {
            using var readOrderDoc = await SearchReadAsync(
                config,
                uid,
                "pos.order",
                [new object[] { "id", "=", createdOrderId }],
                ["id", "name"]
            );
            if (readOrderDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                odooOrderName = GetStringOrDefault(arr[0], "name");
            }
        }
        catch { }

        await SqliteDatabaseService.Instance.BindLocalOrderToOdooOrderAsync(
            payload.LocalOrderId,
            payload.OrderUuid,
            createdOrderId,
            odooOrderName
        );
    }

    private async Task PushCashInOutToOdooAsync(
        OdooConfigRecord config,
        int uid,
        CashInOutSyncPayload payload,
        int? sessionOdooId)
    {
        int activeSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, sessionOdooId);
        string operation = payload.Amount >= 0m ? "in" : "out";
        double absAmount = (double)Math.Abs(payload.Amount);

        try
        {
            using var _ = await ExecuteKwAsync(
                config,
                uid,
                "pos.session",
                "try_cash_in_out",
                [
                    new[] { activeSessionId },
                    operation,
                    absAmount,
                    payload.Reason ?? "",
                    new { }
                ]
            );
        }
        catch { }
    }

    private async Task PushCloseSessionToOdooAsync(
        OdooConfigRecord config,
        int uid,
        CloseSessionSyncPayload payload,
        int? sessionOdooId)
    {
        int activeSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, sessionOdooId);

        try
        {
            using var _ = await ExecuteKwAsync(
                config,
                uid,
                "pos.session",
                "post_closing_cash_details",
                [new[] { activeSessionId }, (double)payload.ClosingCashCount]
            );
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(payload.ClosingNote))
        {
            try
            {
                using var _ = await ExecuteKwAsync(
                    config,
                    uid,
                    "pos.session",
                    "update_closing_control_state_session",
                    [new[] { activeSessionId }, payload.ClosingNote]
                );
            }
            catch { }
        }

        try
        {
            using var _ = await ExecuteKwAsync(
                config,
                uid,
                "pos.session",
                "close_session_from_ui",
                [new[] { activeSessionId }, new object[] { }]
            );
        }
        catch
        {
            try
            {
                using var _ = await ExecuteKwAsync(
                    config,
                    uid,
                    "pos.session",
                    "action_pos_session_closing_control",
                    [new[] { activeSessionId }]
                );
            }
            catch { }
        }
    }

    // =========================================================================
    // 1. COMPANIES (res.company)
    // =========================================================================
    public async Task<List<OdooCompanyDto>> FetchOdooCompaniesAsync(OdooConfigRecord? overrideConfig = null)
    {
        var config = overrideConfig ?? await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooCompaniesInternalAsync(config, uid);
    }

    private async Task<List<OdooCompanyDto>> FetchOdooCompaniesInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "res.company",
            "id", "name", "street", "street2", "city", "state_id", "zip", "country_id",
            "vat", "company_registry", "l10n_ma_ice", "ice", "currency_id",
            "phone", "email", "website", "catchall_domain", "color", "primary_color",
            "parent_id", "logo", "avatar_1920");

        using var doc = await SearchReadAsync(config, uid, "res.company", [], fields);

        var list = new List<OdooCompanyDto>();
        if (doc.RootElement.TryGetProperty("result", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string street = GetStringOrDefault(item, "street");
                string street2 = GetStringOrDefault(item, "street2");
                string city = GetStringOrDefault(item, "city");
                var (_, stateName) = GetMany2One(item, "state_id");
                string zip = GetStringOrDefault(item, "zip");
                var (_, countryName) = GetMany2One(item, "country_id");
                string vat = GetStringOrDefault(item, "vat");
                string ice = GetStringOrDefault(item, "l10n_ma_ice",
                    GetStringOrDefault(item, "ice",
                        GetStringOrDefault(item, "company_registry")));
                var (_, currencyName) = GetMany2One(item, "currency_id");
                string phone = GetStringOrDefault(item, "phone");
                string email = GetStringOrDefault(item, "email");
                string website = GetStringOrDefault(item, "website");
                string emailDomain = GetStringOrDefault(item, "catchall_domain");
                int colorIdx = GetIntOrDefault(item, "color", 2);
                string colorHex = GetStringOrDefault(item, "primary_color",
                    colorIdx > 0 ? PosCategoryRecord.GetOdooColorHex(colorIdx) : "#D97736");
                var (parentId, parentName) = GetMany2One(item, "parent_id");
                string? logo = GetNullableString(item, "logo", "avatar_1920");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new OdooCompanyDto(
                        odooId, name, street, street2, city, stateName, zip, countryName,
                        vat, ice, string.IsNullOrWhiteSpace(currencyName) ? "MAD" : currencyName,
                        phone, email, website, emailDomain, colorIdx, colorHex,
                        parentId, parentName, logo
                    ));
                }
            }
        }
        return list;
    }

    // =========================================================================
    // 2. USERS (res.users)
    // =========================================================================
    public async Task<List<OdooUserDto>> FetchOdooUsersAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooUsersInternalAsync(config, uid);
    }

    private async Task<List<OdooUserDto>> FetchOdooUsersInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "res.users",
            "id", "name", "login", "email", "phone", "mobile", "state", "lang", "share",
            "image_1920", "avatar_1920", "image_128", "avatar_128");

        using var doc = await SearchReadAsync(
            config,
            uid,
            "res.users",
            [new object[] { "active", "=", true }],
            fields
        );

        var list = new List<OdooUserDto>();
        if (doc.RootElement.TryGetProperty("result", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string login = GetStringOrDefault(item, "login");
                string email = GetStringOrDefault(item, "email");
                string phone = GetStringOrDefault(item, "phone", GetStringOrDefault(item, "mobile"));
                string stateRaw = GetStringOrDefault(item, "state", "active");
                string status = string.Equals(stateRaw, "active", StringComparison.OrdinalIgnoreCase)
                    ? "Confirmed"
                    : "Invited";

                string langRaw = GetStringOrDefault(item, "lang", "en_US");
                string language = langRaw switch
                {
                    "fr_FR" => "French (FR)",
                    "ar_001" or "ar_SY" => "Arabic",
                    "es_ES" => "Spanish",
                    _ => "English (US)"
                };

                string role = "User";
                if (odooId == 2 || name.Contains("Admin", StringComparison.OrdinalIgnoreCase) || login.Contains("admin", StringComparison.OrdinalIgnoreCase))
                    role = "Administrator";
                else if (name.Contains("Light", StringComparison.OrdinalIgnoreCase) || login.Equals("light", StringComparison.OrdinalIgnoreCase))
                    role = "Light";

                string? avatar = GetNullableString(item, "image_1920", "image_128", "avatar_1920", "avatar_128");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(login))
                {
                    list.Add(new OdooUserDto(odooId, name, login, email, phone, avatar, role, status, language));
                }
            }
        }
        return list;
    }

    // =========================================================================
    // 3. POS PAYMENT METHODS (pos.payment.method)
    // =========================================================================
    public async Task<List<PosPaymentMethodDto>> FetchOdooPaymentMethodsAsync(OdooConfigRecord? overrideConfig = null)
    {
        var config = overrideConfig ?? await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooPaymentMethodsInternalAsync(config, uid);
    }

    private async Task<List<PosPaymentMethodDto>> FetchOdooPaymentMethodsInternalAsync(OdooConfigRecord config, int uid)
    {
        var shopMap = new Dictionary<int, string>();
        try
        {
            var shopFields = await FilterValidFieldsAsync(config, uid, "pos.config", "id", "name");
            using var shopDoc = await SearchReadAsync(config, uid, "pos.config", [], shopFields);
            if (shopDoc.RootElement.TryGetProperty("result", out var sList) && sList.ValueKind == JsonValueKind.Array)
            {
                foreach (var sItem in sList.EnumerateArray())
                {
                    int sId = GetIntOrDefault(sItem, "id");
                    string sName = GetStringOrDefault(sItem, "name");
                    if (sId > 0 && !string.IsNullOrWhiteSpace(sName))
                        shopMap[sId] = sName;
                }
            }
        }
        catch { }

        var fields = await FilterValidFieldsAsync(config, uid, "pos.payment.method",
            "id", "name", "type", "journal_id", "company_id", "config_ids", "use_payment_terminal", "image");

        using var doc = await SearchReadAsync(config, uid, "pos.payment.method", [], fields);

        var list = new List<PosPaymentMethodDto>();
        if (doc.RootElement.TryGetProperty("result", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string type = GetStringOrDefault(item, "type", "cash");
                var (journalId, journalName) = GetMany2One(item, "journal_id");
                var (companyId, companyName) = GetMany2One(item, "company_id");

                var shopNames = new List<string>();
                foreach (int cfgId in GetIdArray(item, "config_ids"))
                {
                    shopNames.Add(shopMap.TryGetValue(cfgId, out var sName) ? sName : $"POS {cfgId}");
                }
                string linkedShops = string.Join(", ", shopNames);

                string integration = GetStringOrDefault(item, "use_payment_terminal", "None required");
                string? imageBase64 = GetNullableString(item, "image");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new PosPaymentMethodDto(
                        odooId, name, type, journalId, journalName,
                        companyId, companyName, linkedShops, integration, imageBase64
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // 4. POS CATEGORIES (pos.category)
    // =========================================================================
    public async Task<List<PosCategoryDto>> FetchOdooCategoriesAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooCategoriesInternalAsync(config, uid);
    }

    private async Task<List<PosCategoryDto>> FetchOdooCategoriesInternalAsync(OdooConfigRecord config, int uid)
    {
        var shopMap = new Dictionary<int, string>();
        var categoryToShopsMap = new Dictionary<int, HashSet<string>>();

        try
        {
            var shopFields = await FilterValidFieldsAsync(config, uid, "pos.config",
                "id", "name", "limit_categories", "iface_available_categ_ids");

            using var shopDoc = await SearchReadAsync(config, uid, "pos.config", [], shopFields);
            if (shopDoc.RootElement.TryGetProperty("result", out var sList) && sList.ValueKind == JsonValueKind.Array)
            {
                foreach (var sItem in sList.EnumerateArray())
                {
                    int sId = GetIntOrDefault(sItem, "id");
                    string sName = GetStringOrDefault(sItem, "name");
                    if (sId > 0 && !string.IsNullOrWhiteSpace(sName))
                    {
                        shopMap[sId] = sName;
                        foreach (int cId in GetIdArray(sItem, "iface_available_categ_ids"))
                        {
                            if (!categoryToShopsMap.TryGetValue(cId, out var set))
                            {
                                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                categoryToShopsMap[cId] = set;
                            }
                            set.Add(sName);
                        }
                    }
                }
            }
        }
        catch { }

        var catFields = await FilterValidFieldsAsync(config, uid, "pos.category",
            "id", "name", "parent_id", "sequence", "color", "image_128", "pos_config_ids");

        using var doc = await SearchReadAsync(config, uid, "pos.category", [], catFields);

        var list = new List<PosCategoryDto>();
        if (doc.RootElement.TryGetProperty("result", out var resultList) && resultList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                var (parentId, parentName) = GetMany2One(item, "parent_id");
                int sequence = GetIntOrDefault(item, "sequence", 10);
                int color = GetIntOrDefault(item, "color", 0);
                string colorHex = PosCategoryRecord.GetOdooColorHex(color);
                string? imageBase64 = GetNullableString(item, "image_128");

                var shops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (int cfgId in GetIdArray(item, "pos_config_ids"))
                {
                    shops.Add(shopMap.TryGetValue(cfgId, out var sName) ? sName : $"POS {cfgId}");
                }
                if (categoryToShopsMap.TryGetValue(odooId, out var mappedShops))
                {
                    foreach (var s in mappedShops) shops.Add(s);
                }

                string linkedShops = string.Join(", ", shops);
                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new PosCategoryDto(odooId, name, parentId, string.IsNullOrWhiteSpace(parentName) ? null : parentName, linkedShops, color, colorHex, sequence, imageBase64));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // 5. PRODUCT ATTRIBUTES & VALUES (product.attribute & product.attribute.value)
    // =========================================================================
    public async Task<(List<ProductAttributeDto> attributes, List<ProductAttributeValueDto> values)> FetchOdooAttributesAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooAttributesInternalAsync(config, uid);
    }

    private async Task<(List<ProductAttributeDto> attributes, List<ProductAttributeValueDto> values)> FetchOdooAttributesInternalAsync(
        OdooConfigRecord config,
        int uid)
    {
        var attrFields = await FilterValidFieldsAsync(config, uid, "product.attribute",
            "id", "name", "display_type", "create_variant", "number_related_products", "product_tmpl_ids", "value_ids");

        using var attrDoc = await SearchReadAsync(config, uid, "product.attribute", [], attrFields);

        var attributes = new List<ProductAttributeDto>();
        if (attrDoc.RootElement.TryGetProperty("result", out var attrList) && attrList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in attrList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string displayType = GetStringOrDefault(item, "display_type", "radio");
                string createVariant = GetStringOrDefault(item, "create_variant", "always");
                int relatedCount = GetIntOrDefault(item, "number_related_products", -1);
                if (relatedCount < 0)
                    relatedCount = GetIdArray(item, "product_tmpl_ids").Count;
                var valIds = GetIdArray(item, "value_ids");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    attributes.Add(new ProductAttributeDto(odooId, name, displayType, createVariant, relatedCount, valIds));
                }
            }
        }

        var valFields = await FilterValidFieldsAsync(config, uid, "product.attribute.value",
            "id", "attribute_id", "name", "is_custom", "html_color", "image", "default_extra_price");

        using var valDoc = await SearchReadAsync(config, uid, "product.attribute.value", [], valFields);

        var values = new List<ProductAttributeValueDto>();
        if (valDoc.RootElement.TryGetProperty("result", out var valList) && valList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in valList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                var (attrId, _) = GetMany2One(item, "attribute_id");
                bool isCustom = GetBoolOrDefault(item, "is_custom");
                string? htmlColor = GetNullableString(item, "html_color");
                string? imageBase64 = GetNullableString(item, "image");
                double extraPrice = (double)GetDecimalOrDefault(item, "default_extra_price");

                if (odooId > 0 && attrId.HasValue && attrId.Value > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    values.Add(new ProductAttributeValueDto(odooId, attrId.Value, name, isCustom, htmlColor, imageBase64, extraPrice));
                }
            }
        }

        return (attributes, values);
    }

    // =========================================================================
    // 6. PRODUCT TAGS (product.tag)
    // =========================================================================
    public async Task<List<ProductTagDto>> FetchOdooTagsAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooTagsInternalAsync(config, uid);
    }

    private async Task<List<ProductTagDto>> FetchOdooTagsInternalAsync(OdooConfigRecord config, int uid)
    {
        var tagFields = await FilterValidFieldsAsync(config, uid, "product.tag",
            "id", "name", "color", "product_template_ids", "product_ids");

        using var tagDoc = await SearchReadAsync(config, uid, "product.tag", [], tagFields);

        var tags = new List<ProductTagDto>();
        if (tagDoc.RootElement.TryGetProperty("result", out var tagList) && tagList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tagList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                int color = GetIntOrDefault(item, "color");
                int productsCount = GetIdArray(item, "product_template_ids").Count;
                if (productsCount == 0)
                    productsCount = GetIdArray(item, "product_ids").Count;

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    tags.Add(new ProductTagDto(odooId, name, color, productsCount));
                }
            }
        }

        return tags;
    }

    // =========================================================================
    // 7. COMBO CHOICES (product.combo / product.combo.item OR pos.combo / pos.combo.line)
    // =========================================================================
    public async Task<(List<ComboChoiceDto> combos, List<ComboChoiceLineDto> lines)> FetchOdooComboChoicesAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooComboChoicesInternalAsync(config, uid);
    }

    private async Task<(List<ComboChoiceDto> combos, List<ComboChoiceLineDto> lines)> FetchOdooComboChoicesInternalAsync(
        OdooConfigRecord config,
        int uid)
    {
        var productComboFields = await GetAvailableFieldsAsync(config, uid, "product.combo");
        string comboModel = productComboFields.Count > 0 ? "product.combo" : "pos.combo";
        string lineModel = productComboFields.Count > 0 ? "product.combo.item" : "pos.combo.line";

        var combos = new List<ComboChoiceDto>();
        var lines = new List<ComboChoiceLineDto>();

        try
        {
            var cFields = await FilterValidFieldsAsync(config, uid, comboModel,
                "id", "name", "combo_item_ids", "combo_line_ids");

            using var comboDoc = await SearchReadAsync(config, uid, comboModel, [], cFields);

            if (comboDoc.RootElement.TryGetProperty("result", out var cList) && cList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cList.EnumerateArray())
                {
                    int odooId = GetIntOrDefault(item, "id");
                    string name = GetStringOrDefault(item, "name");
                    var lineIds = GetIdArray(item, "combo_item_ids");
                    if (lineIds.Count == 0)
                        lineIds = GetIdArray(item, "combo_line_ids");

                    if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                    {
                        combos.Add(new ComboChoiceDto(odooId, name, lineIds));
                    }
                }
            }

            var lFields = await FilterValidFieldsAsync(config, uid, lineModel,
                "id", "combo_id", "product_id", "extra_price", "combo_price", "lst_price");

            using var lineDoc = await SearchReadAsync(config, uid, lineModel, [], lFields);

            var productIdsToLookupPrice = new HashSet<int>();
            var rawLines = new List<(int odooId, int comboId, int? prodId, string prodName, decimal origPrice, decimal extraPrice)>();

            if (lineDoc.RootElement.TryGetProperty("result", out var lList) && lList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in lList.EnumerateArray())
                {
                    int odooId = GetIntOrDefault(item, "id");
                    var (comboId, _) = GetMany2One(item, "combo_id");
                    var (prodId, prodName) = GetMany2One(item, "product_id");
                    decimal extraPrice = GetDecimalOrDefault(item, "extra_price", GetDecimalOrDefault(item, "combo_price", 0.00m));
                    decimal origPrice = GetDecimalOrDefault(item, "lst_price", 0.00m);

                    if (prodId.HasValue && origPrice == 0.00m)
                        productIdsToLookupPrice.Add(prodId.Value);

                    if (odooId > 0 && comboId.HasValue)
                    {
                        rawLines.Add((odooId, comboId.Value, prodId, prodName, origPrice, extraPrice));
                    }
                }
            }

            var prodPriceMap = new Dictionary<int, decimal>();
            if (productIdsToLookupPrice.Count > 0)
            {
                try
                {
                    using var pDoc = await SearchReadAsync(
                        config, uid, "product.product",
                        [new object[] { "id", "in", productIdsToLookupPrice.ToArray() }],
                        ["id", "lst_price"]
                    );
                    if (pDoc.RootElement.TryGetProperty("result", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pEl in pArr.EnumerateArray())
                        {
                            int pid = GetIntOrDefault(pEl, "id");
                            decimal price = GetDecimalOrDefault(pEl, "lst_price");
                            if (pid > 0) prodPriceMap[pid] = price;
                        }
                    }
                }
                catch { }
            }

            foreach (var rl in rawLines)
            {
                decimal origPrice = rl.origPrice;
                if (origPrice == 0.00m && rl.prodId.HasValue && prodPriceMap.TryGetValue(rl.prodId.Value, out decimal lookupPrice))
                {
                    origPrice = lookupPrice;
                }
                lines.Add(new ComboChoiceLineDto(rl.odooId, rl.comboId, rl.prodId, rl.prodName, origPrice, rl.extraPrice));
            }
        }
        catch { }

        return (combos, lines);
    }

    // =========================================================================
    // 8. PRODUCTS (product.template)
    // =========================================================================
    public async Task<List<ProductDto>> FetchOdooProductsAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooProductsInternalAsync(config, uid);
    }

    private async Task<List<ProductDto>> FetchOdooProductsInternalAsync(
        OdooConfigRecord config,
        int uid,
        List<PosCategoryDto>? preloadedCats = null,
        List<ProductTagDto>? preloadedTags = null,
        List<ComboChoiceDto>? preloadedCombos = null,
        List<ProductAttributeValueDto>? preloadedAttrVals = null)
    {
        var posCategoryMap = new Dictionary<int, string>();
        try
        {
            var cats = preloadedCats ?? await FetchOdooCategoriesInternalAsync(config, uid);
            foreach (var c in cats) posCategoryMap[c.OdooId] = c.Name;
        }
        catch { }

        var tagMap = new Dictionary<int, string>();
        try
        {
            var tags = preloadedTags ?? await FetchOdooTagsInternalAsync(config, uid);
            foreach (var t in tags) tagMap[t.OdooId] = t.Name;
        }
        catch { }

        var comboMap = new Dictionary<int, (string Name, int Count)>();
        try
        {
            var combos = preloadedCombos ?? (await FetchOdooComboChoicesInternalAsync(config, uid)).combos;
            foreach (var c in combos) comboMap[c.OdooId] = (c.Name, c.LineIds.Count);
        }
        catch { }

        var taxMap = new Dictionary<int, (string Label, decimal Rate)>();
        try
        {
            var taxFields = await FilterValidFieldsAsync(config, uid, "account.tax", "id", "name", "amount");
            using var taxDoc = await SearchReadAsync(config, uid, "account.tax", [], taxFields);
            if (taxDoc.RootElement.TryGetProperty("result", out var tList) && tList.ValueKind == JsonValueKind.Array)
            {
                foreach (var tItem in tList.EnumerateArray())
                {
                    int tid = GetIntOrDefault(tItem, "id");
                    decimal amt = GetDecimalOrDefault(tItem, "amount", 15m);
                    string tName = GetStringOrDefault(tItem, "name", $"{amt:0.#}%");
                    if (tid > 0) taxMap[tid] = (tName, amt);
                }
            }
        }
        catch { }

        var templateAttrLinesMap = new Dictionary<int, List<object>>();
        try
        {
            var ptalFields = await FilterValidFieldsAsync(config, uid, "product.template.attribute.line",
                "id", "product_tmpl_id", "attribute_id", "value_ids");

            using var ptalDoc = await SearchReadAsync(config, uid, "product.template.attribute.line", [], ptalFields);

            var attrVals = preloadedAttrVals ?? (await FetchOdooAttributesInternalAsync(config, uid)).values;
            var valNameMap = attrVals.ToDictionary(v => v.OdooId, v => v.Name);

            if (ptalDoc.RootElement.TryGetProperty("result", out var ptalList) && ptalList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ptalList.EnumerateArray())
                {
                    var (tmplId, _) = GetMany2One(item, "product_tmpl_id");
                    var (_, attrName) = GetMany2One(item, "attribute_id");
                    var valIds = GetIdArray(item, "value_ids");
                    var valNames = valIds
                        .Select(id => valNameMap.TryGetValue(id, out var vn) ? vn : null)
                        .Where(vn => !string.IsNullOrWhiteSpace(vn))
                        .ToList();

                    if (tmplId.HasValue && !string.IsNullOrWhiteSpace(attrName))
                    {
                        if (!templateAttrLinesMap.TryGetValue(tmplId.Value, out var list))
                        {
                            list = [];
                            templateAttrLinesMap[tmplId.Value] = list;
                        }
                        list.Add(new
                        {
                            AttributeName = attrName,
                            Values = string.Join(", ", valNames)
                        });
                    }
                }
            }
        }
        catch { }

        var prodFields = await FilterValidFieldsAsync(config, uid, "product.template",
            "id", "name", "type", "detailed_type", "list_price", "standard_price",
            "default_code", "barcode", "pos_categ_ids", "pos_categ_id", "categ_id",
            "product_variant_count", "qty_available", "sale_ok", "purchase_ok",
            "available_in_pos", "tracking", "is_storable", "to_weight",
            "taxes_id", "supplier_taxes_id", "uom_id", "company_id",
            "public_description", "description_sale",
            "image_1920", "image_128", "product_tag_ids",
            "attribute_line_ids", "combo_ids");

        using var prodDoc = await SearchReadAsync(
            config, uid, "product.template",
            [new object[] { "active", "=", true }],
            prodFields
        );

        var products = new List<ProductDto>();
        if (prodDoc.RootElement.TryGetProperty("result", out var prodList) && prodList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prodList.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string type = GetStringOrDefault(item, "type", GetStringOrDefault(item, "detailed_type", "consu"));
                decimal listPrice = GetDecimalOrDefault(item, "list_price");
                decimal stdPrice = GetDecimalOrDefault(item, "standard_price");
                string sku = GetStringOrDefault(item, "default_code");
                string barcode = GetStringOrDefault(item, "barcode");

                int? catId = null;
                string? catName = null;
                var posCategIds = GetIdArray(item, "pos_categ_ids");
                if (posCategIds.Count > 0)
                {
                    catId = posCategIds[0];
                    if (posCategoryMap.TryGetValue(catId.Value, out var resolvedCatName))
                        catName = resolvedCatName;
                }
                if (!catId.HasValue)
                {
                    var (singleCatId, singleCatName) = GetMany2One(item, "pos_categ_id");
                    if (singleCatId.HasValue)
                    {
                        catId = singleCatId;
                        catName = singleCatName;
                    }
                }

                var (_, rawInternalCateg) = GetMany2One(item, "categ_id");
                string internalCat = string.IsNullOrWhiteSpace(rawInternalCateg)
                    ? "All"
                    : rawInternalCateg.Split('/').Last().Trim();

                int variantCount = Math.Max(1, GetIntOrDefault(item, "product_variant_count", 1));
                decimal qtyAvailable = GetDecimalOrDefault(item, "qty_available");
                bool saleOk = GetBoolOrDefault(item, "sale_ok", true);
                bool purchaseOk = GetBoolOrDefault(item, "purchase_ok", true);
                bool availableInPos = GetBoolOrDefault(item, "available_in_pos", true);
                string tracking = GetStringOrDefault(item, "tracking", "none");
                bool trackInventory = GetBoolOrDefault(item, "is_storable", !string.Equals(tracking, "none", StringComparison.OrdinalIgnoreCase));
                bool toWeight = GetBoolOrDefault(item, "to_weight");

                string salesTaxLabel = "15%";
                decimal salesTaxRate = 15.0m;
                var salesTaxIds = GetIdArray(item, "taxes_id");
                if (salesTaxIds.Count > 0 && taxMap.TryGetValue(salesTaxIds[0], out var sTax))
                {
                    salesTaxLabel = sTax.Label;
                    salesTaxRate = sTax.Rate;
                }

                string purchaseTaxLabel = "15%";
                var purchaseTaxIds = GetIdArray(item, "supplier_taxes_id");
                if (purchaseTaxIds.Count > 0 && taxMap.TryGetValue(purchaseTaxIds[0], out var pTax))
                {
                    purchaseTaxLabel = pTax.Label;
                }

                var (_, uomName) = GetMany2One(item, "uom_id");
                var (_, companyName) = GetMany2One(item, "company_id");
                string posDesc = GetStringOrDefault(item, "public_description", GetStringOrDefault(item, "description_sale"));

                string? imageBase64 = GetNullableString(item, "image_128", "image_1920");

                string? tagsJson = null;
                var tagIds = GetIdArray(item, "product_tag_ids");
                if (tagIds.Count > 0)
                {
                    var tagNames = tagIds
                        .Select(id => tagMap.TryGetValue(id, out var tn) ? tn : null)
                        .Where(vn => !string.IsNullOrWhiteSpace(vn))
                        .ToList();
                    if (tagNames.Count > 0)
                        tagsJson = JsonSerializer.Serialize(tagNames);
                }

                string? attributesJson = null;
                if (templateAttrLinesMap.TryGetValue(odooId, out var attrObjs) && attrObjs.Count > 0)
                {
                    attributesJson = JsonSerializer.Serialize(attrObjs);
                }

                string? combosJson = null;
                var comboIds = GetIdArray(item, "combo_ids");
                if (comboIds.Count > 0)
                {
                    var comboObjs = new List<object>();
                    foreach (int cid in comboIds)
                    {
                        if (comboMap.TryGetValue(cid, out var cInfo))
                        {
                            comboObjs.Add(new
                            {
                                ComboName = cInfo.Name,
                                ItemsCount = $"{cInfo.Count} {(cInfo.Count == 1 ? "item" : "items")}"
                            });
                        }
                    }
                    if (comboObjs.Count > 0)
                        combosJson = JsonSerializer.Serialize(comboObjs);
                }

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    products.Add(new ProductDto(
                        odooId, name, type, listPrice, stdPrice,
                        sku, barcode, catId, catName, variantCount,
                        imageBase64, tagsJson, attributesJson, combosJson,
                        internalCat, qtyAvailable, saleOk, purchaseOk,
                        availableInPos, trackInventory, toWeight,
                        salesTaxLabel, salesTaxRate, purchaseTaxLabel,
                        string.IsNullOrWhiteSpace(uomName) ? "Units" : uomName,
                        companyName, posDesc
                    ));
                }
            }
        }

        return products;
    }

    // =========================================================================
    // 9. POS SESSIONS (pos.session)
    // =========================================================================
    public async Task<List<PosSessionDto>> FetchOdooSessionsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooSessionsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosSessionDto>> FetchOdooSessionsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        await ProcessPendingQueueAsync(config);
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooSessionsInternalAsync(config, uid);
    }

    private async Task<List<PosSessionDto>> FetchOdooSessionsInternalAsync(OdooConfigRecord config, int uid, Dictionary<int, string?>? userAvatarMap = null)
    {
        if (userAvatarMap == null)
        {
            userAvatarMap = [];
            try
            {
                var users = await FetchOdooUsersInternalAsync(config, uid);
                foreach (var u in users) userAvatarMap[u.OdooId] = u.AvatarBase64;
            }
            catch { }
        }

        var fields = await FilterValidFieldsAsync(config, uid, "pos.session",
            "id", "name", "config_id", "user_id", "start_at", "stop_at", "state",
            "cash_register_balance_start", "cash_register_balance_end_real",
            "order_count", "total_payments_amount");

        using var doc = await SearchReadAsync(config, uid, "pos.session", [], fields);

        var list = new List<PosSessionDto>();
        if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                var (cfgId, cfgName) = GetMany2One(item, "config_id");
                var (userId, userName) = GetMany2One(item, "user_id");
                DateTime? startAt = GetDateTimeOrNull(item, "start_at");
                DateTime? stopAt = GetDateTimeOrNull(item, "stop_at");
                string state = GetStringOrDefault(item, "state", "opened");
                decimal openBal = GetDecimalOrDefault(item, "cash_register_balance_start");
                decimal closeBal = GetDecimalOrDefault(item, "cash_register_balance_end_real");
                int ordersCount = GetIntOrDefault(item, "order_count");
                decimal totalPayments = GetDecimalOrDefault(item, "total_payments_amount");

                string? userAvatar = userId.HasValue && userAvatarMap.TryGetValue(userId.Value, out var av) ? av : null;

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new PosSessionDto(
                        odooId, name, cfgId, cfgName, userId, userName,
                        userAvatar, startAt, stopAt, state, openBal, closeBal,
                        ordersCount, totalPayments, userName
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // 10. POS ORDERS & ORDER LINES (pos.order & pos.order.line)
    // =========================================================================
    public async Task<(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments)> FetchOdooOrdersAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooOrdersAsync(config, allowOfflineSimulation);
    }

    public async Task<(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments)> FetchOdooOrdersAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        await ProcessPendingQueueAsync(config);
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooOrdersInternalAsync(config, uid);
    }

    private async Task<(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments)> FetchOdooOrdersInternalAsync(
        OdooConfigRecord config,
        int uid)
    {
        var orderFields = await FilterValidFieldsAsync(config, uid, "pos.order",
            "id", "name", "pos_reference", "tracking_number", "session_id", "config_id",
            "date_order", "user_id", "cashier", "partner_id", "fiscal_position_id",
            "amount_total", "amount_tax", "amount_paid", "margin", "margin_percent", "margin_ratio",
            "state", "is_edited", "company_id", "general_note", "note", "refunded_order_ids");

        using var doc = await SearchReadAsync(config, uid, "pos.order", [], orderFields);

        var orders = new List<PosOrderDto>();
        if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                string posRef = GetStringOrDefault(item, "pos_reference");
                string trackNum = GetStringOrDefault(item, "tracking_number");
                var (sessOdooId, sessName) = GetMany2One(item, "session_id");
                var (_, cfgNameFromField) = GetMany2One(item, "config_id");
                string cfgName = !string.IsNullOrWhiteSpace(cfgNameFromField)
                    ? cfgNameFromField
                    : (sessName.Contains('/') ? sessName.Split('/')[0].Trim() : "Point of Sale");

                DateTime? dateOrder = GetDateTimeOrNull(item, "date_order");
                var (cashierId, userName) = GetMany2One(item, "user_id");
                string cashierName = GetStringOrDefault(item, "cashier", userName);
                var (_, fiscalName) = GetMany2One(item, "fiscal_position_id");
                string fiscal = string.IsNullOrWhiteSpace(fiscalName) ? "Standard Tax" : fiscalName;
                var (_, companyName) = GetMany2One(item, "company_id");
                string company = string.IsNullOrWhiteSpace(companyName) ? "My Company" : companyName;

                decimal amountTotal = GetDecimalOrDefault(item, "amount_total");
                decimal amountTax = GetDecimalOrDefault(item, "amount_tax");
                decimal amountPaid = GetDecimalOrDefault(item, "amount_paid", amountTotal);
                decimal margin = GetDecimalOrDefault(item, "margin", 0.00m);
                decimal marginRatio = GetDecimalOrDefault(item, "margin_percent", GetDecimalOrDefault(item, "margin_ratio", 0.00m));
                string state = GetStringOrDefault(item, "state", "paid");
                bool isEdited = GetBoolOrDefault(item, "is_edited");
                string note = GetStringOrDefault(item, "general_note", GetStringOrDefault(item, "note"));

                var refundedIds = GetIdArray(item, "refunded_order_ids");
                string? refundedIdsJson = refundedIds.Count > 0 ? JsonSerializer.Serialize(refundedIds) : null;

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    orders.Add(new PosOrderDto(
                        odooId, name, posRef, trackNum, null, sessOdooId,
                        sessName, cfgName, dateOrder, cashierId, cashierName,
                        null, fiscal, amountTotal, amountTax, amountPaid,
                        margin, marginRatio, state, isEdited, company, note, refundedIdsJson, null
                    ));
                }
            }
        }

        var lines = new List<PosOrderLineDto>();
        try
        {
            var lineFields = await FilterValidFieldsAsync(config, uid, "pos.order.line",
                "id", "order_id", "product_id", "full_product_name", "name",
                "qty", "price_unit", "discount", "price_subtotal", "price_subtotal_incl");

            using var lineDoc = await SearchReadAsync(config, uid, "pos.order.line", [], lineFields);

            if (lineDoc.RootElement.TryGetProperty("result", out var lItems) && lItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var lItem in lItems.EnumerateArray())
                {
                    int lineOdooId = GetIntOrDefault(lItem, "id");
                    var (orderOdooId, _) = GetMany2One(lItem, "order_id");
                    var (prodId, prodNameFromRel) = GetMany2One(lItem, "product_id");
                    string prodName = GetStringOrDefault(lItem, "full_product_name",
                        !string.IsNullOrWhiteSpace(prodNameFromRel) ? prodNameFromRel : GetStringOrDefault(lItem, "name"));

                    decimal qty = GetDecimalOrDefault(lItem, "qty", 1.00m);
                    decimal priceUnit = GetDecimalOrDefault(lItem, "price_unit");
                    decimal discount = GetDecimalOrDefault(lItem, "discount");
                    decimal subTotal = GetDecimalOrDefault(lItem, "price_subtotal");
                    decimal subTotalIncl = GetDecimalOrDefault(lItem, "price_subtotal_incl");

                    string taxLabel = subTotal != 0 && subTotalIncl != subTotal
                        ? $"{Math.Round(Math.Abs((subTotalIncl - subTotal) / subTotal) * 100m, 0)}%"
                        : "0%";

                    if (lineOdooId > 0 && orderOdooId.HasValue)
                    {
                        lines.Add(new PosOrderLineDto(
                            lineOdooId, orderOdooId.Value, prodId, prodName,
                            false, qty, priceUnit, discount, taxLabel, subTotal, subTotalIncl
                        ));
                    }
                }
            }
        }
        catch { }

        var payments = await FetchOdooPaymentsInternalAsync(config, uid);
        return (orders, lines, payments);
    }

    // =========================================================================
    // 11. POS PAYMENTS (pos.payment)
    // =========================================================================
    public async Task<List<PosPaymentDto>> FetchOdooPaymentsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooPaymentsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosPaymentDto>> FetchOdooPaymentsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        await ProcessPendingQueueAsync(config);
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooPaymentsInternalAsync(config, uid);
    }

    private async Task<List<PosPaymentDto>> FetchOdooPaymentsInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "pos.payment",
            "id", "pos_order_id", "session_id", "payment_method_id", "amount", "payment_date", "currency_id", "cashier");

        using var doc = await SearchReadAsync(config, uid, "pos.payment", [], fields);

        var list = new List<PosPaymentDto>();
        if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                var (orderOdooId, orderName) = GetMany2One(item, "pos_order_id");
                var (sessionOdooId, sessionName) = GetMany2One(item, "session_id");
                var (methodId, methodName) = GetMany2One(item, "payment_method_id");
                var (_, currencyName) = GetMany2One(item, "currency_id");
                decimal amount = GetDecimalOrDefault(item, "amount");
                DateTime? payDate = GetDateTimeOrNull(item, "payment_date");
                string cashier = GetStringOrDefault(item, "cashier", "");

                if (odooId > 0)
                {
                    list.Add(new PosPaymentDto(
                        odooId,
                        orderOdooId,
                        orderName,
                        sessionOdooId,
                        sessionName,
                        cashier,
                        methodId,
                        string.IsNullOrWhiteSpace(methodName) ? "Cash" : methodName,
                        amount,
                        string.IsNullOrWhiteSpace(currencyName) ? "MAD" : currencyName,
                        "—",
                        payDate
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // 12. POS CONFIGURATIONS (pos.config)
    // =========================================================================
    public async Task<List<PosConfigDto>> FetchOdooPosConfigsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooPosConfigsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosConfigDto>> FetchOdooPosConfigsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        await ProcessPendingQueueAsync(config);
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooPosConfigsInternalAsync(config, uid);
    }

    private async Task<List<PosConfigDto>> FetchOdooPosConfigsInternalAsync(
        OdooConfigRecord config,
        int uid,
        List<PosSessionDto>? preloadedSessions = null)
    {
        var sessions = preloadedSessions ?? await FetchOdooSessionsInternalAsync(config, uid);
        var sessionsById = sessions.ToDictionary(s => s.OdooId, s => s);
        var sessionsByConfigId = sessions
            .Where(s => s.PosConfigId.HasValue)
            .GroupBy(s => s.PosConfigId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OdooId).Take(5).Reverse().ToList());

        var fields = await FilterValidFieldsAsync(config, uid, "pos.config",
            "id", "name", "current_session_id", "current_session_state",
            "last_session_closing_date", "last_session_closing_cash", "pos_session_username");

        using var doc = await SearchReadAsync(config, uid, "pos.config", [], fields);

        var list = new List<PosConfigDto>();
        if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                int odooId = GetIntOrDefault(item, "id");
                string name = GetStringOrDefault(item, "name");
                var (sessId, sessName) = GetMany2One(item, "current_session_id");
                string state = GetStringOrDefault(item, "current_session_state", "closed");
                DateTime? lastCloseDate = GetDateTimeOrNull(item, "last_session_closing_date");
                decimal lastCloseCash = GetDecimalOrDefault(item, "last_session_closing_cash");

                decimal openingBal = 0.00m;
                int? activeUserId = null;
                string? activeUserName = GetNullableString(item, "pos_session_username");
                string? activeUserAvatar = null;

                if (sessId.HasValue && sessionsById.TryGetValue(sessId.Value, out var activeSess))
                {
                    openingBal = activeSess.OpeningBalance;
                    activeUserId = activeSess.UserId;
                    activeUserAvatar = activeSess.UserAvatarBase64;
                    if (string.IsNullOrWhiteSpace(activeUserName))
                        activeUserName = activeSess.UserName;
                    if (activeSess.StartAt.HasValue && !lastCloseDate.HasValue)
                        lastCloseDate = activeSess.StartAt;
                }

                List<double> salesPoints = [0, 0, 0, 0, 0];
                if (sessionsByConfigId.TryGetValue(odooId, out var recentSessions) && recentSessions.Count > 0)
                {
                    salesPoints = recentSessions.Select(s => (double)s.TotalPaymentsAmount).ToList();
                    while (salesPoints.Count < 5)
                        salesPoints.Insert(0, 0.0);
                }

                string salesJson = JsonSerializer.Serialize(salesPoints);

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new PosConfigDto(
                        odooId,
                        name,
                        sessId,
                        string.IsNullOrWhiteSpace(sessName) ? null : sessName,
                        state,
                        lastCloseDate,
                        lastCloseCash,
                        openingBal,
                        activeUserId,
                        activeUserName,
                        activeUserAvatar,
                        salesJson
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // MASTER SYNCHRONIZATION PIPELINE (FLUSHES OFFLINE QUEUE + PULLS 12 MODELS)
    // =========================================================================
    public async Task<SyncAllSummary> SyncAllFromOdooAsync(
        OdooConfigRecord? overrideConfig = null,
        IProgress<SyncProgressReport>? progress = null)
    {
        var config = overrideConfig ?? await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        const int totalSteps = 12;

        void Report(int step, string module, string message)
        {
            double pct = Math.Round((double)step / totalSteps * 100.0, 0);
            progress?.Report(new SyncProgressReport(step, totalSteps, module, message, pct));
        }

        Report(0, "Authentication", "Authenticating & flushing pending POS transactions...");
        await ProcessPendingQueueAsync(config);

        int uid = await EnsureAuthenticatedUidAsync(config);
        _modelFieldsCache.Clear();

        DateTime now = DateTime.Now;

        // 1. Companies
        Report(1, "Companies", "Fetching & reconciling Companies (res.company)...");
        var companies = await FetchOdooCompaniesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCompaniesAsync(companies);
        await SqliteDatabaseService.Instance.UpdateCompanySyncStatusAsync(now, $"Companies: {companies.Count}");

        // 2. Users
        Report(2, "Users", "Fetching & reconciling Users (res.users)...");
        var users = await FetchOdooUsersInternalAsync(config, uid);
        var (uImp, uUpd, uDel) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(users);
        await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(now, $"Users: {uImp} imported, {uUpd} updated, {uDel} removed");

        // 3. Payment Methods
        Report(3, "Payment Methods", "Fetching & reconciling Payment Methods (pos.payment.method)...");
        var paymentMethods = await FetchOdooPaymentMethodsInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooPaymentMethodsAsync(paymentMethods);
        await SqliteDatabaseService.Instance.UpdatePaymentMethodSyncStatusAsync(now, $"Payment Methods: {paymentMethods.Count}");

        // 4. POS Categories
        Report(4, "PoS Categories", "Fetching & reconciling PoS Categories (pos.category)...");
        var categories = await FetchOdooCategoriesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(categories);
        await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(now, $"Categories: {categories.Count}");

        // 5. Product Attributes & Values
        Report(5, "Attributes", "Fetching & reconciling Product Attributes & Values...");
        var (attrs, attrVals) = await FetchOdooAttributesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(attrs, attrVals);
        await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(now, $"Attributes: {attrs.Count}");

        // 6. Product Tags
        Report(6, "Product Tags", "Fetching & reconciling Product Tags (product.tag)...");
        var tags = await FetchOdooTagsInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(tags);
        await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(now, $"Tags: {tags.Count}");

        // 7. Combo Choices
        Report(7, "Combo Choices", "Fetching & reconciling Combo Choices...");
        var (combos, comboLines) = await FetchOdooComboChoicesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, comboLines);
        await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(now, $"Combos: {combos.Count}");

        // 8. Products (reusing preloaded categories, tags, combos, and attribute values)
        Report(8, "Products", "Fetching & reconciling Products (product.template)...");
        var products = await FetchOdooProductsInternalAsync(config, uid, categories, tags, combos, attrVals);
        await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(products);
        await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(now, $"Products: {products.Count}");

        // 9. POS Sessions
        Report(9, "Sessions", "Fetching & reconciling POS Sessions (pos.session)...");
        var userAvatarMap = users.ToDictionary(u => u.OdooId, u => u.AvatarBase64);
        var sessions = await FetchOdooSessionsInternalAsync(config, uid, userAvatarMap);
        await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessions);
        await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(now, $"Sessions: {sessions.Count}");

        // 10. POS Configurations
        Report(10, "POS Shops", "Fetching & reconciling POS Configurations (pos.config)...");
        var posConfigs = await FetchOdooPosConfigsInternalAsync(config, uid, sessions);
        await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(posConfigs);
        await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(now, $"POS Shops: {posConfigs.Count}");

        // 11. POS Orders & Order Lines
        Report(11, "Orders", "Fetching & reconciling POS Orders & Order Lines...");
        var (orders, orderLines, payments) = await FetchOdooOrdersInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, orderLines, payments);
        await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(now, $"Orders: {orders.Count}");

        // 12. POS Payments
        Report(12, "Payments", "Reconciling POS Payments (pos.payment)...");
        await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(payments);
        await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(now, $"Payments: {payments.Count}");

        string summaryMsg = $"Synchronized {companies.Count} companies, {posConfigs.Count} shops, {products.Count} products, {paymentMethods.Count} payment methods, {orders.Count} orders, {users.Count} users.";
        return new SyncAllSummary(
            true,
            summaryMsg,
            companies.Count,
            users.Count,
            paymentMethods.Count,
            categories.Count,
            attrs.Count,
            tags.Count,
            combos.Count,
            products.Count,
            posConfigs.Count,
            sessions.Count,
            orders.Count,
            payments.Count
        );
    }
}