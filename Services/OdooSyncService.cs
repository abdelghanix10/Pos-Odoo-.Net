using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, (int Uid, DateTime Expires)> _uidCache = new();
    private readonly ConcurrentDictionary<int, (List<(int Id, string Name, string Type, bool IsCash)> Methods, DateTime Expires)> _sessionPmCache = new();
    private readonly ConcurrentDictionary<(int Id, int Company), (int VariantId, List<int> TaxIds, DateTime Expires)> _variantTaxCache = new();
    private readonly ConcurrentDictionary<int, (OdooTaxInfo Info, DateTime Expires)> _taxInfoCache = new();
    private readonly ConcurrentDictionary<int, (int SessionOdooId, int? CompanyId, DateTime Expires)> _openSessionByConfigCache = new();
    private int _cachedFallbackProductId;
    private readonly System.Threading.Timer _backgroundFlushTimer;
    private int _flushRunning;

    public PosUserRecord? CurrentPosUser { get; private set; }

    public OdooConfigRecord? CurrentUserOdooConfig { get; private set; }

    public void SetCurrentPosUser(PosUserRecord? user)
    {
        CurrentPosUser = user;
        if (user == null)
        {
            CurrentUserOdooConfig = null;
        }
    }

    public async Task<ConnectionTestResult> AuthenticatePosUserAsync(PosUserRecord user)
    {
        SetCurrentPosUser(user);

        var globalConfig = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        string serverUrl = !string.IsNullOrWhiteSpace(user.OdooServerUrl)
            ? user.OdooServerUrl.Trim()
            : globalConfig.ServerUrl;
        string dbName = !string.IsNullOrWhiteSpace(user.OdooDatabase)
            ? user.OdooDatabase.Trim()
            : globalConfig.DatabaseName;

        if (user.HasConfiguredOdooCredentials &&
            !string.IsNullOrWhiteSpace(serverUrl) &&
            !string.IsNullOrWhiteSpace(dbName))
        {
            var userConfig = new OdooConfigRecord(
                serverUrl,
                dbName,
                user.Login.Trim(),
                user.Password,
                globalConfig.LastSyncedAt,
                globalConfig.LastStatus
            );

            CurrentUserOdooConfig = userConfig;

            if (await CheckServerReachableAsync(serverUrl))
            {
                var authResult = await TestConnectionAsync(userConfig);
                if (authResult.Success && authResult.Uid.HasValue && authResult.Uid.Value > 0)
                {
                    if (user.OdooId != authResult.Uid.Value)
                    {
                        user.OdooId = authResult.Uid.Value;
                        await SqliteDatabaseService.Instance.SaveUserRecordAsync(user);
                    }
                    return authResult;
                }
                return authResult;
            }
        }

        await RecordUserLoginInOdooAsync(user);
        return new ConnectionTestResult(true, $"Logged in locally as {user.Name}", user.OdooId);
    }

    private async Task<(OdooConfigRecord ExecConfig, int ExecUid)> ResolveExecutionCredentialsAsync(
        OdooConfigRecord fallbackConfig,
        int fallbackUid,
        int? targetUserId,
        string? targetUserNameOrLogin)
    {
        try
        {
            // 1. Check if CurrentPosUser matches and has configured credentials
            if (CurrentPosUser != null && CurrentPosUser.HasConfiguredOdooCredentials)
            {
                bool matchesCurrent =
                    (!targetUserId.HasValue && string.IsNullOrWhiteSpace(targetUserNameOrLogin)) ||
                    (targetUserId.HasValue && CurrentPosUser.OdooId == targetUserId.Value) ||
                    (!string.IsNullOrWhiteSpace(targetUserNameOrLogin) &&
                     (string.Equals(CurrentPosUser.Name, targetUserNameOrLogin, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(CurrentPosUser.Login, targetUserNameOrLogin, StringComparison.OrdinalIgnoreCase)));

                if (matchesCurrent)
                {
                    var uCfg = CurrentUserOdooConfig ?? new OdooConfigRecord(
                        !string.IsNullOrWhiteSpace(CurrentPosUser.OdooServerUrl) ? CurrentPosUser.OdooServerUrl : fallbackConfig.ServerUrl,
                        !string.IsNullOrWhiteSpace(CurrentPosUser.OdooDatabase) ? CurrentPosUser.OdooDatabase : fallbackConfig.DatabaseName,
                        CurrentPosUser.Login.Trim(),
                        CurrentPosUser.Password,
                        fallbackConfig.LastSyncedAt,
                        fallbackConfig.LastStatus
                    );
                    int uUid = await EnsureAuthenticatedUidAsync(uCfg);
                    return (uCfg, uUid);
                }
            }

            // 2. Look up the specific user's credentials from the SQLite Users table
            var dbUserConfig = await SqliteDatabaseService.Instance.GetUserSpecificOdooConfigAsync(targetUserId, targetUserNameOrLogin);
            if (dbUserConfig != null)
            {
                int uUid = await EnsureAuthenticatedUidAsync(dbUserConfig);
                return (dbUserConfig, uUid);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResolveExecutionCredentialsAsync] Fallback to default config: {ex.Message}");
        }

        return (fallbackConfig, fallbackUid);
    }

    public bool IsOnline { get; private set; }
    public string ConnectionStatusLabel { get; private set; } = "Offline (Local Mode)";
    public event Action<bool>? OnlineStatusChanged;
    public event Action<int>? PendingQueueFlushed;
    public event Action? QueueUpdated;

    private OdooSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        // Retry pending/failed queue items in the background so uploads and
        // session closes complete even when the cashier is idle.
        _backgroundFlushTimer = new System.Threading.Timer(async _ =>
        {
            if (Interlocked.CompareExchange(ref _flushRunning, 1, 0) != 0) return;
            try
            {
                await ProcessPendingQueueAsync();
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _flushRunning, 0);
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void NotifyQueueUpdated()
    {
        try
        {
            QueueUpdated?.Invoke();
        }
        catch { }
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
        string cacheKey = $"{NormalizeUrl(config.ServerUrl)}|{config.DatabaseName}|{config.Username}|{config.ApiKey}";
        if (_uidCache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Uid;
        }

        var test = await TestConnectionAsync(config);
        if (!test.Success || !test.Uid.HasValue)
        {
            _uidCache.TryRemove(cacheKey, out _);
            throw new InvalidOperationException(test.Message);
        }
        _uidCache[cacheKey] = (test.Uid.Value, DateTime.UtcNow.AddMinutes(5));
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
        // Wait for the queue lock so no caller's request is ever dropped
        await _queueLock.WaitAsync();

        int processedCount = 0;
        try
        {
            var initialItems = await SqliteDatabaseService.Instance.GetPendingSyncQueueAsync();
            if (initialItems.Count == 0)
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
            var attemptedInThisRun = new HashSet<long>();
            var blockedConfigIds = new HashSet<int>();
            bool blockGlobal = false;

            while (true)
            {
                // Re-query on each iteration so any SessionOdooId bound by OPEN_SESSION
                // or any newly enqueued order is picked up immediately in FIFO order.
                // Per-shop blocking: a failed item blocks only its own pos.config so a
                // stuck CLOSE_SESSION never lets a later OPEN_SESSION create a duplicate.
                var queueItems = await SqliteDatabaseService.Instance.GetPendingSyncQueueAsync();
                var nextItem = queueItems.FirstOrDefault(i =>
                    !attemptedInThisRun.Contains(i.Id) &&
                    !blockGlobal &&
                    (!i.PosConfigOdooId.HasValue || !blockedConfigIds.Contains(i.PosConfigOdooId.Value)));
                if (nextItem == null)
                    break;

                // If the next operation is CREATE_ORDER, batch all consecutive ready CREATE_ORDER
                // items for the same shop into a single upload (1 search_read, 1 create, 1 payment create, 1 paid).
                if (nextItem.OperationType == "CREATE_ORDER")
                {
                    var batchItems = new List<(PendingSyncItemRecord QueueItem, CreateOrderSyncPayload Payload, int? EffectiveSessionOdooId)>();

                    foreach (var candidate in queueItems)
                    {
                        if (attemptedInThisRun.Contains(candidate.Id)) continue;
                        if (candidate.PosConfigOdooId != nextItem.PosConfigOdooId) continue;

                        // Stop batching when a non-CREATE_ORDER op (e.g. CLOSE_SESSION) appears for this
                        // shop so strict FIFO order is preserved.
                        if (candidate.OperationType != "CREATE_ORDER")
                            break;

                        attemptedInThisRun.Add(candidate.Id);
                        var payload = JsonSerializer.Deserialize<CreateOrderSyncPayload>(candidate.PayloadJson);
                        if (payload != null)
                        {
                            int? resolvedLocalOdooId = payload.LocalSessionId.HasValue && payload.LocalSessionId.Value > 0
                                ? await SqliteDatabaseService.Instance.GetOdooSessionIdForLocalSessionAsync(payload.LocalSessionId.Value)
                                : null;
                            int? effectiveSessionOdooId = resolvedLocalOdooId ?? candidate.SessionOdooId ?? payload.SessionOdooId;
                            batchItems.Add((candidate, payload, effectiveSessionOdooId));
                        }
                    }

                    if (batchItems.Count > 0)
                    {
                        try
                        {
                            await PushPosOrdersBatchToOdooAsync(config, uid, batchItems);
                            foreach (var b in batchItems)
                            {
                                await SqliteDatabaseService.Instance.MarkSyncQueueItemCompletedAsync(b.QueueItem.Id);
                                processedCount++;
                            }
                            NotifyQueueUpdated();
                            continue;
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                        {
                            UpdateStatus(false, "Offline (Local Mode)");
                            await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(nextItem.Id, ex.Message);
                            NotifyQueueUpdated();
                            break;
                        }
                        catch (Exception ex)
                        {
                            foreach (var b in batchItems)
                            {
                                await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(b.QueueItem.Id, ex.Message);
                            }
                            NotifyQueueUpdated();
                            if (nextItem.PosConfigOdooId.HasValue)
                                blockedConfigIds.Add(nextItem.PosConfigOdooId.Value);
                            else
                                blockGlobal = true;
                            continue;
                        }
                    }
                }

                attemptedInThisRun.Add(nextItem.Id);

                try
                {
                    switch (nextItem.OperationType)
                    {
                        case "OPEN_SESSION":
                        {
                            var payload = JsonSerializer.Deserialize<OpenSessionSyncPayload>(nextItem.PayloadJson);
                            if (payload != null)
                            {
                                await PushOpenSessionToOdooAsync(config, uid, payload);
                            }
                            break;
                        }
                        case "CREATE_ORDER":
                        {
                            // Safety net: the batch path above normally consumes CREATE_ORDER items.
                            // If one reaches here (e.g. it was skipped there), upload it as a single-item batch.
                            var payload = JsonSerializer.Deserialize<CreateOrderSyncPayload>(nextItem.PayloadJson);
                            if (payload != null)
                            {
                                int? resolvedLocalOdooId = payload.LocalSessionId.HasValue && payload.LocalSessionId.Value > 0
                                    ? await SqliteDatabaseService.Instance.GetOdooSessionIdForLocalSessionAsync(payload.LocalSessionId.Value)
                                    : null;
                                int? effectiveSessionOdooId = resolvedLocalOdooId ?? nextItem.SessionOdooId;
                                await PushPosOrdersBatchToOdooAsync(config, uid, [(nextItem, payload, effectiveSessionOdooId)]);
                            }
                            break;
                        }
                        case "CASH_IN_OUT":
                        {
                            var payload = JsonSerializer.Deserialize<CashInOutSyncPayload>(nextItem.PayloadJson);
                            if (payload != null)
                            {
                                int? resolvedLocalOdooId = payload.LocalSessionId.HasValue && payload.LocalSessionId.Value > 0
                                    ? await SqliteDatabaseService.Instance.GetOdooSessionIdForLocalSessionAsync(payload.LocalSessionId.Value)
                                    : null;
                                int? effectiveSessionOdooId = resolvedLocalOdooId ?? nextItem.SessionOdooId;
                                await PushCashInOutToOdooAsync(config, uid, payload, effectiveSessionOdooId);
                            }
                            break;
                        }
                        case "CLOSE_SESSION":
                        {
                            var payload = JsonSerializer.Deserialize<CloseSessionSyncPayload>(nextItem.PayloadJson);
                            if (payload != null)
                            {
                                int? resolvedLocalOdooId = payload.LocalSessionId.HasValue && payload.LocalSessionId.Value > 0
                                    ? await SqliteDatabaseService.Instance.GetOdooSessionIdForLocalSessionAsync(payload.LocalSessionId.Value)
                                    : null;
                                int? effectiveSessionOdooId = resolvedLocalOdooId ?? nextItem.SessionOdooId ?? payload.SessionOdooId;
                                await PushCloseSessionToOdooAsync(config, uid, payload, effectiveSessionOdooId);
                            }
                            break;
                        }
                    }

                    await SqliteDatabaseService.Instance.MarkSyncQueueItemCompletedAsync(nextItem.Id);
                    processedCount++;
                    NotifyQueueUpdated();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Network dropped mid-queue: stop immediately to preserve strict sequential order
                    UpdateStatus(false, "Offline (Local Mode)");
                    await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(nextItem.Id, ex.Message);
                    NotifyQueueUpdated();
                    break;
                }
                catch (Exception ex)
                {
                    await SqliteDatabaseService.Instance.MarkSyncQueueItemFailedAsync(nextItem.Id, ex.Message);
                    NotifyQueueUpdated();

                    // Block the failed item's shop so dependent operations (later orders,
                    // CLOSE_SESSION, OPEN_SESSION) cannot overtake it this run.
                    if (nextItem.OperationType is "OPEN_SESSION" or "CREATE_ORDER" or "CASH_IN_OUT" or "CLOSE_SESSION")
                    {
                        if (nextItem.PosConfigOdooId.HasValue)
                            blockedConfigIds.Add(nextItem.PosConfigOdooId.Value);
                        else
                            blockGlobal = true;
                    }
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

    public async Task<int?> ResolvePosUserOdooIdAsync(OdooConfigRecord config, int uid, int? knownUserId, string? userNameOrLogin)
    {
        if (knownUserId.HasValue && knownUserId.Value > 0)
            return knownUserId.Value;

        if (CurrentPosUser?.OdooId is > 0 &&
            (string.IsNullOrWhiteSpace(userNameOrLogin) ||
             string.Equals(CurrentPosUser.Name, userNameOrLogin, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(CurrentPosUser.Login, userNameOrLogin, StringComparison.OrdinalIgnoreCase)))
        {
            return CurrentPosUser.OdooId.Value;
        }

        if (!string.IsNullOrWhiteSpace(userNameOrLogin))
        {
            try
            {
                var localUsers = await SqliteDatabaseService.Instance.LoadAllUsersAsync();
                var localMatch = localUsers.FirstOrDefault(u =>
                    u.OdooId is > 0 &&
                    (string.Equals(u.Name, userNameOrLogin.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(u.Login, userNameOrLogin.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (localMatch?.OdooId is > 0)
                    return localMatch.OdooId.Value;

                using var userDoc = await SearchReadAsync(
                    config,
                    uid,
                    "res.users",
                    [
                        "|",
                        new object[] { "login", "=", userNameOrLogin.Trim() },
                        new object[] { "name", "=", userNameOrLogin.Trim() }
                    ],
                    ["id", "name", "login"]
                );
                if (userDoc.RootElement.TryGetProperty("result", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array &&
                    arr.GetArrayLength() > 0)
                {
                    int resolvedId = GetIntOrDefault(arr[0], "id");
                    if (resolvedId > 0)
                        return resolvedId;
                }
            }
            catch { }
        }

        return CurrentPosUser?.OdooId;
    }

    public async Task RecordUserLoginInOdooAsync(PosUserRecord user)
    {
        if (user == null) return;

        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl) ||
                string.IsNullOrWhiteSpace(config.DatabaseName) ||
                string.IsNullOrWhiteSpace(config.Username))
            {
                return;
            }

            if (!await CheckServerReachableAsync(config.ServerUrl))
                return;

            int adminUid = await EnsureAuthenticatedUidAsync(config);

            // 1. Ensure user.OdooId is resolved from Odoo res.users
            if (!user.OdooId.HasValue || user.OdooId.Value <= 0)
            {
                int? resolvedId = await ResolvePosUserOdooIdAsync(config, adminUid, user.OdooId, !string.IsNullOrWhiteSpace(user.Login) ? user.Login : user.Name);
                if (resolvedId.HasValue && resolvedId.Value > 0)
                {
                    user.OdooId = resolvedId.Value;
                    await SqliteDatabaseService.Instance.SaveUserRecordAsync(user);
                }
            }

            // 2. If the user has their own Odoo password configured, authenticate directly so Odoo logs their login
            if (!string.IsNullOrWhiteSpace(user.Login) &&
                !string.IsNullOrWhiteSpace(user.Password) &&
                user.Password != "••••••••")
            {
                string baseUrl = NormalizeUrl(config.ServerUrl);
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
                            user.Login.Trim(),
                            user.Password,
                            new { }
                        }
                    },
                    id = Interlocked.Increment(ref _rpcIdCounter)
                };

                using var jsonContent = new StringContent(JsonSerializer.Serialize(authPayload), Encoding.UTF8, "application/json");
                using var resp = await _httpClient.PostAsync($"{baseUrl}/jsonrpc", jsonContent);
                if (resp.IsSuccessStatusCode)
                {
                    string respStr = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respStr);
                    if (doc.RootElement.TryGetProperty("result", out var res) &&
                        res.ValueKind == JsonValueKind.Number &&
                        res.TryGetInt32(out int loggedInUid) &&
                        loggedInUid > 0 &&
                        user.OdooId != loggedInUid)
                    {
                        user.OdooId = loggedInUid;
                        await SqliteDatabaseService.Instance.SaveUserRecordAsync(user);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecordUserLoginInOdooAsync] {ex.Message}");
        }
    }

    private async Task<int> EnsureOdooOpenSessionIdAsync(OdooConfigRecord config, int uid, int configOdooId, int? knownSessionOdooId = null, long? preferLocalSessionId = null, int? sessionUserId = null)
    {
        int effectiveUserId = (sessionUserId.HasValue && sessionUserId.Value > 0)
            ? sessionUserId.Value
            : (CurrentPosUser?.OdooId is > 0 ? CurrentPosUser.OdooId.Value : uid);

        // 1. Prefer the known session when it is still open and not owned by another local session
        if (knownSessionOdooId.HasValue && knownSessionOdooId.Value > 0)
        {
            using var checkDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [
                    new object[] { "id", "=", knownSessionOdooId.Value },
                    new object[] { "config_id", "=", configOdooId }
                ],
                ["id", "name", "state"]
            );
            if (checkDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                string state = GetStringOrDefault(arr[0], "state", "opened");
                if (!string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase) &&
                    await IsSessionFreeForLocalAsync(knownSessionOdooId.Value, preferLocalSessionId))
                {
                    return knownSessionOdooId.Value;
                }
            }
        }

        // 2. Otherwise look for an existing non-closed session on Odoo for this pos.config
        // that is not already bound to a different local session
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
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    int candidateId = GetIntOrDefault(el, "id");
                    if (candidateId > 0 && await IsSessionFreeForLocalAsync(candidateId, preferLocalSessionId))
                    {
                        return candidateId;
                    }
                }
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
                    ["user_id"] = effectiveUserId
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
        var (execConfig, execUid) = await ResolveExecutionCredentialsAsync(config, uid, payload.UserId, payload.CashierName);
        config = execConfig;
        uid = execUid;

        int? resolvedUserId = await ResolvePosUserOdooIdAsync(config, uid, payload.UserId, payload.CashierName);
        int odooSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, payload.ExistingSessionOdooId, payload.LocalSessionId, resolvedUserId);

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

        string? odooSessionName = null;
        try
        {
            using var sessDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", odooSessionId }],
                ["id", "name", "state", "bank_statement_id"]
            );
            if (sessDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                var el = arr[0];
                odooSessionName = GetStringOrDefault(el, "name");

                int stmtId = 0;
                if (el.TryGetProperty("bank_statement_id", out var bVal))
                {
                    if (bVal.ValueKind == JsonValueKind.Array && bVal.GetArrayLength() > 0)
                        stmtId = bVal[0].GetInt32();
                    else if (bVal.ValueKind == JsonValueKind.Number)
                        stmtId = bVal.GetInt32();
                }

                if (stmtId == 0)
                {
                    try
                    {
                        using var _ = await ExecuteKwAsync(
                            config,
                            uid,
                            "pos.session",
                            "action_pos_session_open",
                            [new[] { odooSessionId }]
                        );
                        using var sessDoc2 = await SearchReadAsync(
                            config,
                            uid,
                            "pos.session",
                            [new object[] { "id", "=", odooSessionId }],
                            ["id", "bank_statement_id"]
                        );
                        if (sessDoc2.RootElement.TryGetProperty("result", out var arr2) &&
                            arr2.ValueKind == JsonValueKind.Array &&
                            arr2.GetArrayLength() > 0)
                        {
                            var el2 = arr2[0];
                            if (el2.TryGetProperty("bank_statement_id", out var bVal2))
                            {
                                if (bVal2.ValueKind == JsonValueKind.Array && bVal2.GetArrayLength() > 0)
                                    stmtId = bVal2[0].GetInt32();
                                else if (bVal2.ValueKind == JsonValueKind.Number)
                                    stmtId = bVal2.GetInt32();
                            }
                        }
                    }
                    catch { }
                }

                if (stmtId > 0)
                {
                    try
                    {
                        using var _ = await ExecuteKwAsync(
                            config,
                            uid,
                            "account.bank.statement",
                            "write",
                            [
                                new[] { stmtId },
                                new Dictionary<string, object>
                                {
                                    ["balance_start"] = (double)payload.OpeningBalance
                                }
                            ]
                        );
                    }
                    catch { }
                }

                // Write start_at (Opening Date) and opening notes directly to pos.session
                try
                {
                    var sessVals = new Dictionary<string, object>
                    {
                        ["start_at"] = payload.OpenedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    if (resolvedUserId.HasValue && resolvedUserId.Value > 0)
                    {
                        sessVals["user_id"] = resolvedUserId.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(payload.OpeningNote))
                    {
                        sessVals["opening_notes"] = payload.OpeningNote;
                    }
                    using var _ = await ExecuteKwAsync(
                        config,
                        uid,
                        "pos.session",
                        "write",
                        [new[] { odooSessionId }, sessVals]
                    );
                }
                catch { }
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

    private async Task<bool> IsSessionFreeForLocalAsync(int odooSessionId, long? preferLocalSessionId)
    {
        if (!preferLocalSessionId.HasValue) return true;
        var owner = await SqliteDatabaseService.Instance.GetLocalSessionIdByOdooSessionIdAsync(odooSessionId);
        return !owner.HasValue || owner.Value == preferLocalSessionId.Value;
    }

    private async Task<Dictionary<int, (int VariantId, List<int> TaxIds)>> ResolveProductVariantsAndTaxesAsync(
        OdooConfigRecord config,
        int uid,
        IEnumerable<int> templateIds,
        int? targetCompanyId = null)
    {
        var map = new Dictionary<int, (int VariantId, List<int> TaxIds)>();
        var distinctIds = templateIds.Where(id => id > 0).Distinct().ToArray();
        if (distinctIds.Length == 0) return map;

        int companyKey = targetCompanyId ?? 0;
        var missingIds = new List<int>();
        foreach (int id in distinctIds)
        {
            if (_variantTaxCache.TryGetValue((id, companyKey), out var cached) && cached.Expires > DateTime.UtcNow)
                map[id] = (cached.VariantId, cached.TaxIds);
            else
                missingIds.Add(id);
        }
        if (missingIds.Count == 0) return map;
        distinctIds = missingIds.ToArray();

        try
        {
            var fields = await FilterValidFieldsAsync(config, uid, "product.product", "id", "product_tmpl_id", "taxes_id");
            using var doc = await SearchReadAsync(
                config,
                uid,
                "product.product",
                [
                    "|",
                    new object[] { "product_tmpl_id", "in", distinctIds },
                    new object[] { "id", "in", distinctIds }
                ],
                fields
            );

            if (doc.RootElement.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var allTaxIds = new HashSet<int>();
                var rawItems = new List<(int ProdVariantId, int? TmplId, List<int> TaxIds)>();

                foreach (var el in arr.EnumerateArray())
                {
                    int prodVariantId = GetIntOrDefault(el, "id");
                    var (tmplId, _) = GetMany2One(el, "product_tmpl_id");
                    var taxIds = GetIdArray(el, "taxes_id");
                    foreach (var tid in taxIds) allTaxIds.Add(tid);
                    rawItems.Add((prodVariantId, tmplId, taxIds));
                }

                // If targetCompanyId is known, filter out taxes belonging to other companies to prevent crossover errors
                bool taxLookupSucceeded = false;
                var validTaxIds = new HashSet<int>();
                if (targetCompanyId.HasValue && targetCompanyId.Value > 0 && allTaxIds.Count > 0)
                {
                    try
                    {
                        using var taxDoc = await SearchReadAsync(
                            config,
                            uid,
                            "account.tax",
                            [new object[] { "id", "in", allTaxIds.ToArray() }],
                            ["id", "company_id"]
                        );

                        if (taxDoc.RootElement.TryGetProperty("result", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
                        {
                            taxLookupSucceeded = true;
                            foreach (var tEl in tArr.EnumerateArray())
                            {
                                int tid = GetIntOrDefault(tEl, "id");
                                var (tCompId, _) = GetMany2One(tEl, "company_id");
                                if (tCompId == null || tCompId.Value == targetCompanyId.Value)
                                {
                                    validTaxIds.Add(tid);
                                }
                            }
                        }
                    }
                    catch { }
                }

                foreach (var item in rawItems)
                {
                    var filteredTaxes = taxLookupSucceeded
                        ? item.TaxIds.Where(validTaxIds.Contains).ToList()
                        : item.TaxIds;

                    if (item.ProdVariantId > 0)
                    {
                        var expires = DateTime.UtcNow.AddMinutes(10);
                        if (item.TmplId.HasValue && !map.ContainsKey(item.TmplId.Value))
                        {
                            map[item.TmplId.Value] = (item.ProdVariantId, filteredTaxes);
                            _variantTaxCache[(item.TmplId.Value, companyKey)] = (item.ProdVariantId, filteredTaxes, expires);
                        }
                        if (!map.ContainsKey(item.ProdVariantId))
                        {
                            map[item.ProdVariantId] = (item.ProdVariantId, filteredTaxes);
                            _variantTaxCache[(item.ProdVariantId, companyKey)] = (item.ProdVariantId, filteredTaxes, expires);
                        }
                    }
                }
            }
        }
        catch { }

        return map;
    }

    private static int ExtractSequenceNumber(string? posReference, string? orderName)
    {
        if (!string.IsNullOrWhiteSpace(posReference))
        {
            string[] parts = posReference.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) && seq > 0)
            {
                return seq;
            }
        }

        if (!string.IsNullOrWhiteSpace(orderName))
        {
            int dash = orderName.LastIndexOf('-');
            if (dash >= 0 && dash + 1 < orderName.Length &&
                int.TryParse(orderName[(dash + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) && seq > 0)
            {
                return seq;
            }
        }

        return 1;
    }

    private sealed record OdooTaxInfo(int Id, decimal Amount, string AmountType, bool PriceInclude);

    private async Task<Dictionary<int, OdooTaxInfo>> FetchTaxInfoMapAsync(
        OdooConfigRecord config, int uid, IEnumerable<int> taxIds)
    {
        var map = new Dictionary<int, OdooTaxInfo>();
        var distinct = taxIds.Where(id => id > 0).Distinct().ToArray();
        if (distinct.Length == 0) return map;

        var missing = new List<int>();
        DateTime now = DateTime.UtcNow;
        foreach (int id in distinct)
        {
            if (_taxInfoCache.TryGetValue(id, out var cached) && cached.Expires > now)
                map[id] = cached.Info;
            else
                missing.Add(id);
        }

        if (missing.Count == 0) return map;

        try
        {
            var fields = await FilterValidFieldsAsync(config, uid, "account.tax", "id", "amount", "amount_type", "price_include");
            using var doc = await SearchReadAsync(config, uid, "account.tax", [new object[] { "id", "in", missing.ToArray() }], fields);
            if (doc.RootElement.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                DateTime expires = now.AddMinutes(30);
                foreach (var el in arr.EnumerateArray())
                {
                    int id = GetIntOrDefault(el, "id");
                    decimal amt = GetDecimalOrDefault(el, "amount");
                    string amtType = GetStringOrDefault(el, "amount_type", "percent");
                    bool priceIncl = GetBoolOrDefault(el, "price_include", false);
                    if (id > 0)
                    {
                        var info = new OdooTaxInfo(id, amt, amtType, priceIncl);
                        map[id] = info;
                        _taxInfoCache[id] = (info, expires);
                    }
                }
            }
        }
        catch { }

        return map;
    }

    // =========================================================================
    // PURE LOCAL C# TAX & LINE CALIBRATION MATH (MATCHING ODOO HALF-UP ROUNDING)
    // =========================================================================
    private static decimal OdooRound(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static (decimal PriceSubtotal, decimal PriceSubtotalIncl) ComputeLocalLineTaxAmounts(
        List<int> taxIds,
        Dictionary<int, OdooTaxInfo> taxInfoMap,
        decimal unitPrice,
        decimal qty,
        decimal discount,
        decimal fallbackTaxRate = 0m)
    {
        decimal effUnit = unitPrice * (1.0m - (discount / 100.0m));
        decimal raw = effUnit * qty;

        if (taxIds.Count == 0)
        {
            if (fallbackTaxRate > 0m)
            {
                decimal excl = OdooRound(raw);
                decimal tax = OdooRound(excl * (fallbackTaxRate / 100.0m));
                return (excl, excl + tax);
            }
            decimal amt = OdooRound(raw);
            return (amt, amt);
        }

        decimal sumInclRate = 0m;
        foreach (int tid in taxIds)
        {
            if (taxInfoMap.TryGetValue(tid, out var t) &&
                !string.Equals(t.AmountType, "fixed", StringComparison.OrdinalIgnoreCase) &&
                t.PriceInclude)
            {
                sumInclRate += t.Amount;
            }
        }

        // Base tax-excluded amount rounded using Odoo's HALF-UP rule
        decimal baseAmt = sumInclRate > 0m
            ? OdooRound(raw / (1.0m + (sumInclRate / 100.0m)))
            : OdooRound(raw);

        // Sum each price-excluded tax individually using Odoo's HALF-UP rule
        decimal exclTaxTotal = 0m;
        foreach (int tid in taxIds)
        {
            if (!taxInfoMap.TryGetValue(tid, out var t)) continue;
            if (string.Equals(t.AmountType, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                exclTaxTotal += OdooRound(t.Amount * qty);
            }
            else if (!t.PriceInclude)
            {
                exclTaxTotal += OdooRound(baseAmt * (t.Amount / 100.0m));
            }
        }

        decimal inclAmt = sumInclRate > 0m
            ? OdooRound(raw) + exclTaxTotal
            : baseAmt + exclTaxTotal;

        return (baseAmt, inclAmt);
    }

    private static (decimal CalibratedPriceUnit, decimal SubExcl, decimal SubIncl) CalibrateLineToTargetInclLocal(
        List<int> taxIds,
        Dictionary<int, OdooTaxInfo> taxInfoMap,
        decimal targetIncl,
        decimal qty,
        decimal discount,
        decimal fallbackUnitPrice,
        decimal fallbackTaxRate = 0m)
    {
        decimal mult = qty * (1.0m - (discount / 100.0m));
        if (qty == 0m || (1.0m - (discount / 100.0m)) <= 0m)
        {
            return (fallbackUnitPrice, 0m, 0m);
        }

        // 1. First check if the original fallbackUnitPrice already produces the target (within 1 cent)
        if (fallbackUnitPrice > 0m)
        {
            var (directExcl, directIncl) = ComputeLocalLineTaxAmounts(
                taxIds, taxInfoMap, fallbackUnitPrice, qty, discount, fallbackTaxRate);

            if (Math.Abs(directIncl - targetIncl) <= 0.02m)
            {
                // Keep the clean product unit price and use the exact Odoo-rounded base & incl
                return (fallbackUnitPrice, directExcl, directIncl);
            }
        }

        if (taxIds.Count == 0)
        {
            decimal unit = Math.Round(targetIncl / mult, 4, MidpointRounding.AwayFromZero);
            decimal amt = OdooRound(unit * mult);
            return (unit, amt, amt);
        }

        decimal sumExclRate = 0m;
        decimal fixedSum = 0m;
        foreach (int tid in taxIds)
        {
            if (!taxInfoMap.TryGetValue(tid, out var t)) continue;
            if (string.Equals(t.AmountType, "fixed", StringComparison.OrdinalIgnoreCase))
                fixedSum += OdooRound(t.Amount * qty);
            else if (!t.PriceInclude)
                sumExclRate += t.Amount;
        }

        decimal exclFactor = 1.0m + (sumExclRate / 100.0m);
        if (exclFactor <= 0m) exclFactor = 1.0m;

        decimal netTargetAfterFixed = targetIncl - fixedSum;
        decimal calibratedUnit = Math.Round(netTargetAfterFixed / (mult * exclFactor), 4, MidpointRounding.AwayFromZero);

        var (finalExcl, finalIncl) = ComputeLocalLineTaxAmounts(
            taxIds, taxInfoMap, calibratedUnit, qty, discount, fallbackTaxRate);

        return (calibratedUnit, finalExcl, finalIncl);
    }

    // =========================================================================
    // FAST BATCH POS ORDER UPLOAD (1 RPC FOR ALL ORDERS, 1 FOR PAYMENTS, 1 FOR PAID)
    // =========================================================================
    private async Task PushPosOrdersBatchToOdooAsync(
        OdooConfigRecord config,
        int uid,
        List<(PendingSyncItemRecord QueueItem, CreateOrderSyncPayload Payload, int? EffectiveSessionOdooId)> batch)
    {
        if (batch.Count == 0) return;

        var firstPayload = batch[0].Payload;
        string effectiveCashierName = !string.IsNullOrWhiteSpace(firstPayload.CashierName)
            ? firstPayload.CashierName
            : (CurrentPosUser?.Name ?? "");

        var (execConfig, execUid) = await ResolveExecutionCredentialsAsync(config, uid, firstPayload.CashierId, effectiveCashierName);
        int? resolvedCashierId = await ResolvePosUserOdooIdAsync(execConfig, execUid, firstPayload.CashierId ?? execUid, effectiveCashierName);

        // 1. Single Batch Idempotency Check for all pos_references in the batch
        var allRefs = batch
            .Select(b => b.Payload.PosReference)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToArray();

        var existingRefMap = new Dictionary<string, (int OdooId, string Name, string State)>(StringComparer.OrdinalIgnoreCase);
        if (allRefs.Length > 0)
        {
            using var existingDoc = await SearchReadAsync(
                execConfig,
                execUid,
                "pos.order",
                [new object[] { "pos_reference", "in", allRefs }],
                ["id", "name", "pos_reference", "state"]
            );

            if (existingDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    int exId = GetIntOrDefault(el, "id");
                    string exName = GetStringOrDefault(el, "name");
                    string exRef = GetStringOrDefault(el, "pos_reference");
                    string exState = GetStringOrDefault(el, "state", "draft");
                    if (exId > 0 && !string.IsNullOrWhiteSpace(exRef))
                    {
                        existingRefMap[exRef] = (exId, exName, exState);
                    }
                }
            }
        }

        var remainingBatch = new List<(PendingSyncItemRecord QueueItem, CreateOrderSyncPayload Payload, int? EffectiveSessionOdooId)>();
        foreach (var item in batch)
        {
            if (!string.IsNullOrWhiteSpace(item.Payload.PosReference) &&
                existingRefMap.TryGetValue(item.Payload.PosReference, out var existing))
            {
                // If an existing order on Odoo was stuck in 'draft' ('New') from an earlier attempt, mark it Paid now
                if (string.Equals(existing.State, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.order", "action_pos_order_paid", [new[] { existing.OdooId }]);
                    }
                    catch { }
                }

                await SqliteDatabaseService.Instance.BindLocalOrderToOdooOrderAsync(
                    item.Payload.LocalOrderId,
                    item.Payload.OrderUuid,
                    existing.OdooId,
                    existing.Name
                );
            }
            else
            {
                remainingBatch.Add(item);
            }
        }

        if (remainingBatch.Count == 0)
            return;

        // 2. Resolve open session ID and company ID (cached per PosConfigOdooId)
        int configOdooId = firstPayload.ConfigOdooId;
        int? knownSessId = remainingBatch[0].EffectiveSessionOdooId;
        int activeSessionId;
        int? sessionCompanyId = null;

        if (_openSessionByConfigCache.TryGetValue(configOdooId, out var sessCached) &&
            sessCached.Expires > DateTime.UtcNow &&
            (!knownSessId.HasValue || knownSessId.Value == sessCached.SessionOdooId))
        {
            activeSessionId = sessCached.SessionOdooId;
            sessionCompanyId = sessCached.CompanyId;
        }
        else
        {
            activeSessionId = await EnsureOdooOpenSessionIdAsync(
                execConfig, execUid, configOdooId, knownSessId, firstPayload.LocalSessionId, resolvedCashierId);

            try
            {
                using var sessStateDoc = await SearchReadAsync(
                    execConfig,
                    execUid,
                    "pos.session",
                    [new object[] { "id", "=", activeSessionId }],
                    ["id", "state", "company_id"]
                );
                if (sessStateDoc.RootElement.TryGetProperty("result", out var sArr) &&
                    sArr.ValueKind == JsonValueKind.Array &&
                    sArr.GetArrayLength() > 0)
                {
                    var sEl = sArr[0];
                    var (cid, _) = GetMany2One(sEl, "company_id");
                    sessionCompanyId = cid;

                    string st = GetStringOrDefault(sEl, "state", "opened");
                    if (string.Equals(st, "opening_control", StringComparison.OrdinalIgnoreCase))
                    {
                        decimal openBal = firstPayload.LocalSessionId.HasValue
                            ? await SqliteDatabaseService.Instance.GetSessionOpeningBalanceAsync((int)firstPayload.LocalSessionId.Value)
                            : 0m;
                        try
                        {
                            using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.session", "set_opening_control", [new[] { activeSessionId }, (double)openBal, ""]);
                        }
                        catch
                        {
                            try
                            {
                                using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.session", "action_pos_session_open", [new[] { activeSessionId }]);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            _openSessionByConfigCache[configOdooId] = (activeSessionId, sessionCompanyId, DateTime.UtcNow.AddMinutes(15));
        }

        // 3. Resolve all product variants & taxes across the entire batch at once (cached in memory)
        var allTmplIds = remainingBatch
            .SelectMany(b => b.Payload.Lines)
            .Where(l => l.ProductOdooId.HasValue)
            .Select(l => l.ProductOdooId!.Value)
            .Distinct();

        var variantMap = await ResolveProductVariantsAndTaxesAsync(execConfig, execUid, allTmplIds, sessionCompanyId);
        var taxInfoMap = await FetchTaxInfoMapAsync(execConfig, execUid, variantMap.Values.SelectMany(v => v.TaxIds));

        int fallbackProductId = variantMap.Values.Select(v => v.VariantId).FirstOrDefault();
        if (fallbackProductId == 0)
        {
            if (_cachedFallbackProductId > 0)
            {
                fallbackProductId = _cachedFallbackProductId;
            }
            else
            {
                using var anyProdDoc = await SearchReadAsync(
                    execConfig,
                    execUid,
                    "product.product",
                    [new object[] { "available_in_pos", "=", true }],
                    ["id"]
                );
                if (anyProdDoc.RootElement.TryGetProperty("result", out var pArr) &&
                    pArr.ValueKind == JsonValueKind.Array &&
                    pArr.GetArrayLength() > 0)
                {
                    fallbackProductId = GetIntOrDefault(pArr[0], "id");
                    _cachedFallbackProductId = fallbackProductId;
                }
            }
        }

        // 4. Resolve allowed payment methods for this session (cached per session)
        List<(int Id, string Name, string Type, bool IsCash)> allowedSessionMethods;
        if (_sessionPmCache.TryGetValue(activeSessionId, out var pmCached) && pmCached.Expires > DateTime.UtcNow)
        {
            allowedSessionMethods = pmCached.Methods;
        }
        else
        {
            allowedSessionMethods = await ResolveSessionPaymentMethodsAsync(execConfig, execUid, activeSessionId, configOdooId);
            _sessionPmCache[activeSessionId] = (allowedSessionMethods, DateTime.UtcNow.AddMinutes(30));
        }

        var localPaymentMethods = await SqliteDatabaseService.Instance.LoadAllPaymentMethodsAsync();
        var orderLineFields = await GetAvailableFieldsAsync(execConfig, execUid, "pos.order.line");
        var orderFields = await GetAvailableFieldsAsync(execConfig, execUid, "pos.order");

        // 5. Build all order dictionaries using 100% LOCAL C# MATH (0 RPCs)
        var orderValsList = new List<Dictionary<string, object>>(remainingBatch.Count);
        var calibratedTotals = new List<decimal>(remainingBatch.Count);

        foreach (var item in remainingBatch)
        {
            var payload = item.Payload;
            decimal calOrderTotal = 0m;
            decimal calOrderTax = 0m;
            var lineTuples = new List<object>(payload.Lines.Count);

            foreach (var l in payload.Lines)
            {
                int prodId = fallbackProductId;
                List<int> taxIds = [];
                if (l.ProductOdooId.HasValue && variantMap.TryGetValue(l.ProductOdooId.Value, out var vInfo))
                {
                    prodId = vInfo.VariantId;
                    taxIds = vInfo.TaxIds;
                }
                else if (l.ProductOdooId.HasValue && l.ProductOdooId.Value > 0)
                {
                    prodId = l.ProductOdooId.Value;
                }

                var (calUnit, subExcl, subIncl) = CalibrateLineToTargetInclLocal(
                    taxIds, taxInfoMap, l.PriceSubtotalIncl, l.Qty, l.Discount, l.PriceUnit, l.TaxRate);

                calOrderTotal += subIncl;
                calOrderTax += (subIncl - subExcl);

                var lineDict = new Dictionary<string, object>
                {
                    ["product_id"] = prodId,
                    ["qty"] = (double)l.Qty,
                    ["price_unit"] = (double)calUnit,
                    ["discount"] = (double)l.Discount,
                    ["price_subtotal"] = (double)subExcl,
                    ["price_subtotal_incl"] = (double)subIncl
                };

                if (orderLineFields.Count == 0 || orderLineFields.Contains("full_product_name"))
                    lineDict["full_product_name"] = l.ProductName;
                if (orderLineFields.Count == 0 || orderLineFields.Contains("name"))
                    lineDict["name"] = l.ProductName;
                if (orderLineFields.Count == 0 || orderLineFields.Contains("tax_ids"))
                    lineDict["tax_ids"] = new object[] { new object[] { 6, 0, taxIds.ToArray() } };
                if (!string.IsNullOrWhiteSpace(l.CustomerNote) && (orderLineFields.Count == 0 || orderLineFields.Contains("customer_note")))
                    lineDict["customer_note"] = l.CustomerNote;

                lineTuples.Add(new object[] { 0, 0, lineDict });
            }

            calibratedTotals.Add(calOrderTotal);

            int seqNumber = ExtractSequenceNumber(payload.PosReference, payload.Name);
            string ordCashierName = !string.IsNullOrWhiteSpace(payload.CashierName) ? payload.CashierName : effectiveCashierName;

            var orderVals = new Dictionary<string, object>
            {
                ["name"] = payload.Name,
                ["session_id"] = activeSessionId,
                ["pos_reference"] = payload.PosReference,
                ["amount_total"] = (double)calOrderTotal,
                ["amount_tax"] = (double)calOrderTax,
                ["amount_paid"] = (double)calOrderTotal,
                ["amount_return"] = 0.0,
                ["lines"] = lineTuples
            };

            if (orderFields.Count == 0 || orderFields.Contains("sequence_number"))
                orderVals["sequence_number"] = seqNumber;
            if (orderFields.Count == 0 || orderFields.Contains("user_id"))
                orderVals["user_id"] = payload.CashierId ?? resolvedCashierId ?? execUid;
            if (orderFields.Count == 0 || orderFields.Contains("date_order"))
                orderVals["date_order"] = payload.DateOrder.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(payload.TrackingNumber) && (orderFields.Count == 0 || orderFields.Contains("tracking_number")))
                orderVals["tracking_number"] = payload.TrackingNumber;
            if (!string.IsNullOrWhiteSpace(ordCashierName) && (orderFields.Count == 0 || orderFields.Contains("cashier")))
                orderVals["cashier"] = ordCashierName;
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

            orderValsList.Add(orderVals);
        }

        // 6. Create all orders in ONE single RPC call
        var createdOrderIds = new List<int>(remainingBatch.Count);
        using (var createOrdersDoc = await ExecuteKwAsync(execConfig, execUid, "pos.order", "create", [orderValsList]))
        {
            if (createOrdersDoc.RootElement.TryGetProperty("result", out var res))
            {
                if (res.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in res.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int oid))
                            createdOrderIds.Add(oid);
                    }
                }
                else if (res.ValueKind == JsonValueKind.Number && res.TryGetInt32(out int singleOid))
                {
                    createdOrderIds.Add(singleOid);
                }
            }
        }

        if (createdOrderIds.Count != remainingBatch.Count || createdOrderIds.Any(id => id <= 0))
            throw new InvalidOperationException("Failed to batch-create pos.order records in Odoo.");

        // 7. Build all pos.payment records across the batch and create them in ONE single RPC call
        var allPaymentValsList = new List<Dictionary<string, object>>();
        for (int i = 0; i < remainingBatch.Count; i++)
        {
            int createdOrderId = createdOrderIds[i];
            var payload = remainingBatch[i].Payload;
            decimal targetOrderTotal = calibratedTotals[i];
            bool hasPartner = payload.PartnerId.HasValue && payload.PartnerId.Value > 0;

            decimal sumPrevPayments = 0m;
            for (int pIdx = 0; pIdx < payload.Payments.Count; pIdx++)
            {
                var pay = payload.Payments[pIdx];
                int methodOdooId = ResolveAllowedPaymentMethodId(
                    pay, hasPartner, allowedSessionMethods, localPaymentMethods);

                // Ensure the sum of payments for the order matches calOrderTotal to the exact cent
                decimal payAmount = (pIdx == payload.Payments.Count - 1)
                    ? Math.Round(targetOrderTotal - sumPrevPayments, 2)
                    : Math.Round(pay.Amount, 2);
                sumPrevPayments += payAmount;

                allPaymentValsList.Add(new Dictionary<string, object>
                {
                    ["pos_order_id"] = createdOrderId,
                    ["session_id"] = activeSessionId,
                    ["payment_method_id"] = methodOdooId,
                    ["amount"] = (double)payAmount,
                    ["payment_date"] = pay.PaymentDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                });
            }
        }

        if (allPaymentValsList.Count > 0)
        {
            using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.payment", "create", [allPaymentValsList]);
        }

        // 8. Mark each created order as Paid (Odoo's action_pos_order_paid has self.ensure_one(), so pass 1 ID per call)
        foreach (int createdOrderId in createdOrderIds)
        {
            try
            {
                using var _ = await ExecuteKwAsync(
                    execConfig,
                    execUid,
                    "pos.order",
                    "action_pos_order_paid",
                    [new[] { createdOrderId }]
                );
            }
            catch
            {
                try
                {
                    using var _ = await ExecuteKwAsync(
                        execConfig,
                        execUid,
                        "pos.order",
                        "write",
                        [
                            new[] { createdOrderId },
                            new Dictionary<string, object> { ["state"] = "paid" }
                        ]
                    );
                }
                catch { }
            }
        }

        // 9. Persist the cashier name on all created orders in 1 batch write (prevents Odoo's _compute_cashier from clearing it)
        if (!string.IsNullOrWhiteSpace(effectiveCashierName) && (orderFields.Count == 0 || orderFields.Contains("cashier")))
        {
            try
            {
                using var _ = await ExecuteKwAsync(
                    execConfig,
                    execUid,
                    "pos.order",
                    "write",
                    [
                        createdOrderIds.ToArray(),
                        new Dictionary<string, object> { ["cashier"] = effectiveCashierName }
                    ]
                );
            }
            catch { }
        }

        // 10. Bind all local orders to their Odoo IDs
        for (int i = 0; i < remainingBatch.Count; i++)
        {
            var payload = remainingBatch[i].Payload;
            await SqliteDatabaseService.Instance.BindLocalOrderToOdooOrderAsync(
                payload.LocalOrderId,
                payload.OrderUuid,
                createdOrderIds[i],
                payload.Name
            );
        }
    }

    private static int ResolveAllowedPaymentMethodId(
        PosPaymentSyncPayload pay,
        bool hasPartner,
        List<(int Id, string Name, string Type, bool IsCash)> allowedSessionMethods,
        List<PosPaymentMethodRecord> localPaymentMethods)
    {
        int? methodOdooId = null;
        string reqName = pay.PaymentMethodName?.Trim() ?? "Cash";
        bool wantsCash = reqName.Contains("Cash", StringComparison.OrdinalIgnoreCase);
        bool wantsCard = reqName.Contains("Card", StringComparison.OrdinalIgnoreCase) || reqName.Contains("Bank", StringComparison.OrdinalIgnoreCase);
        bool wantsAccount = reqName.Contains("Customer", StringComparison.OrdinalIgnoreCase) || reqName.Contains("Account", StringComparison.OrdinalIgnoreCase);

        if (allowedSessionMethods.Count > 0)
        {
            if (pay.PaymentMethodOdooId.HasValue)
            {
                var exactIdMatch = allowedSessionMethods.FirstOrDefault(m => m.Id == pay.PaymentMethodOdooId.Value);
                if (exactIdMatch.Id > 0 && (hasPartner || !string.Equals(exactIdMatch.Type, "pay_later", StringComparison.OrdinalIgnoreCase)))
                {
                    methodOdooId = exactIdMatch.Id;
                }
            }

            if (!methodOdooId.HasValue)
            {
                var nameMatch = allowedSessionMethods.FirstOrDefault(m =>
                    string.Equals(m.Name, reqName, StringComparison.OrdinalIgnoreCase) &&
                    (hasPartner || !string.Equals(m.Type, "pay_later", StringComparison.OrdinalIgnoreCase)));
                if (nameMatch.Id > 0)
                    methodOdooId = nameMatch.Id;
            }

            if (!methodOdooId.HasValue)
            {
                if (wantsCash)
                {
                    var cashMatch = allowedSessionMethods.FirstOrDefault(m =>
                        m.IsCash ||
                        string.Equals(m.Type, "cash", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase));
                    if (cashMatch.Id > 0)
                        methodOdooId = cashMatch.Id;
                }
                else if (wantsCard)
                {
                    var cardMatch = allowedSessionMethods.FirstOrDefault(m =>
                        string.Equals(m.Type, "bank", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Bank", StringComparison.OrdinalIgnoreCase));
                    if (cardMatch.Id > 0)
                        methodOdooId = cardMatch.Id;
                }
                else if (wantsAccount && hasPartner)
                {
                    var accMatch = allowedSessionMethods.FirstOrDefault(m =>
                        string.Equals(m.Type, "pay_later", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("Account", StringComparison.OrdinalIgnoreCase));
                    if (accMatch.Id > 0)
                        methodOdooId = accMatch.Id;
                }
            }

            if (!methodOdooId.HasValue)
            {
                var fallbackAllowed = allowedSessionMethods.FirstOrDefault(m =>
                    !string.Equals(m.Type, "pay_later", StringComparison.OrdinalIgnoreCase));
                methodOdooId = fallbackAllowed.Id > 0 ? fallbackAllowed.Id : allowedSessionMethods[0].Id;
            }
        }
        else
        {
            methodOdooId = pay.PaymentMethodOdooId;
            if (!methodOdooId.HasValue || methodOdooId.Value <= 0)
            {
                var matchedMethod = localPaymentMethods.FirstOrDefault(m =>
                    string.Equals(m.Name, reqName, StringComparison.OrdinalIgnoreCase));
                methodOdooId = matchedMethod?.OdooId;
            }

            if (!methodOdooId.HasValue || methodOdooId.Value <= 0)
            {
                methodOdooId = localPaymentMethods.FirstOrDefault()?.OdooId ?? 1;
            }
        }

        return methodOdooId.Value;
    }

    private async Task<List<(int Id, string Name, string Type, bool IsCash)>> ResolveSessionPaymentMethodsAsync(
        OdooConfigRecord config,
        int uid,
        int activeSessionId,
        int configOdooId)
    {
        var allowedSessionMethods = new List<(int Id, string Name, string Type, bool IsCash)>();
        try
        {
            var allowedIds = new List<int>();
            int resolvedConfigId = configOdooId;

            var sessPmFields = await FilterValidFieldsAsync(config, uid, "pos.session", "id", "config_id", "payment_method_ids");
            using (var sessPmDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", activeSessionId }],
                sessPmFields))
            {
                if (sessPmDoc.RootElement.TryGetProperty("result", out var sArr) &&
                    sArr.ValueKind == JsonValueKind.Array &&
                    sArr.GetArrayLength() > 0)
                {
                    var sEl = sArr[0];
                    var (cId, _) = GetMany2One(sEl, "config_id");
                    if (cId.HasValue && cId.Value > 0)
                        resolvedConfigId = cId.Value;

                    allowedIds = GetIdArray(sEl, "payment_method_ids");
                }
            }

            if (allowedIds.Count == 0 && resolvedConfigId > 0)
            {
                var cfgPmFields = await FilterValidFieldsAsync(config, uid, "pos.config", "id", "payment_method_ids");
                using var cfgPmDoc = await SearchReadAsync(
                    config,
                    uid,
                    "pos.config",
                    [new object[] { "id", "=", resolvedConfigId }],
                    cfgPmFields);
                if (cfgPmDoc.RootElement.TryGetProperty("result", out var cArr) &&
                    cArr.ValueKind == JsonValueKind.Array &&
                    cArr.GetArrayLength() > 0)
                {
                    allowedIds = GetIdArray(cArr[0], "payment_method_ids");
                }
            }

            if (allowedIds.Count > 0)
            {
                var pmFields = await FilterValidFieldsAsync(config, uid, "pos.payment.method", "id", "name", "type", "is_cash_count");
                using var pmDoc = await SearchReadAsync(
                    config,
                    uid,
                    "pos.payment.method",
                    [new object[] { "id", "in", allowedIds.ToArray() }],
                    pmFields);
                if (pmDoc.RootElement.TryGetProperty("result", out var pmArr) &&
                    pmArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pmEl in pmArr.EnumerateArray())
                    {
                        int pmId = GetIntOrDefault(pmEl, "id");
                        string pmName = GetStringOrDefault(pmEl, "name", "Cash");
                        string pmType = GetStringOrDefault(pmEl, "type", "cash");
                        bool isCash = GetBoolOrDefault(pmEl, "is_cash_count", pmType == "cash");
                        if (pmId > 0)
                            allowedSessionMethods.Add((pmId, pmName, pmType, isCash));
                    }
                }
            }
        }
        catch { }
        return allowedSessionMethods;
    }

    private async Task PushCashInOutToOdooAsync(
        OdooConfigRecord config,
        int uid,
        CashInOutSyncPayload payload,
        int? sessionOdooId)
    {
        var (sessUserId, sessUserName) = await SqliteDatabaseService.Instance.GetSessionUserAsync(payload.LocalSessionId, sessionOdooId ?? payload.SessionOdooId);
        var (execConfig, execUid) = await ResolveExecutionCredentialsAsync(config, uid, sessUserId, sessUserName);
        config = execConfig;
        uid = execUid;

        int activeSessionId = await EnsureOdooOpenSessionIdAsync(config, uid, payload.ConfigOdooId, sessionOdooId, payload.LocalSessionId);
        string operation = payload.Amount >= 0m ? "in" : "out";
        double absAmount = (double)Math.Abs(payload.Amount);

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

    private async Task<string> GetOdooSessionStateAsync(OdooConfigRecord config, int uid, int sessionId)
    {
        using var doc = await SearchReadAsync(
            config,
            uid,
            "pos.session",
            [new object[] { "id", "=", sessionId }],
            ["id", "state"]
        );

        if (doc.RootElement.TryGetProperty("result", out var arr) &&
            arr.ValueKind == JsonValueKind.Array &&
            arr.GetArrayLength() > 0)
        {
            return GetStringOrDefault(arr[0], "state", "");
        }
        return "";
    }

    // =========================================================================
    // SESSION REPAIR & CLOSE (RECOVERS CANCELLED/DRAFT ORDERS & BALANCES ENTRIES)
    // =========================================================================
    private async Task<(decimal PriceSubtotal, decimal PriceSubtotalIncl)> ComputeOdooLineTaxAmountsAsync(
        OdooConfigRecord config,
        int uid,
        List<int> taxIds,
        Dictionary<int, OdooTaxInfo> taxInfoMap,
        decimal unitPrice,
        decimal qty,
        decimal discount)
    {
        decimal effUnit = unitPrice * (1.0m - (discount / 100.0m));

        if (taxIds.Count == 0)
        {
            decimal amt = OdooRound(effUnit * qty);
            return (amt, amt);
        }

        try
        {
            using var doc = await ExecuteKwAsync(
                config,
                uid,
                "account.tax",
                "compute_all",
                [taxIds.ToArray(), (double)effUnit],
                new { quantity = (double)qty }
            );

            if (doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
            {
                decimal excl = GetDecimalOrDefault(res, "total_excluded");
                decimal incl = GetDecimalOrDefault(res, "total_included");
                return (OdooRound(excl), OdooRound(incl));
            }
        }
        catch { }

        return ComputeLocalLineTaxAmounts(taxIds, taxInfoMap, unitPrice, qty, discount);
    }

    private async Task RepairSessionOrdersAndPaymentsForCloseAsync(
        OdooConfigRecord config, int uid, int targetSessionId, DateTime closedAt)
    {
        int repairMethodId = 0;
        using (var mDoc = await SearchReadAsync(config, uid, "pos.session",
            [new object[] { "id", "=", targetSessionId }], ["id", "payment_method_ids"]))
        {
            if (mDoc.RootElement.TryGetProperty("result", out var mArr) &&
                mArr.ValueKind == JsonValueKind.Array && mArr.GetArrayLength() > 0)
            {
                var ids = GetIdArray(mArr[0], "payment_method_ids");
                if (ids.Count > 0) repairMethodId = ids[0];
            }
        }

        // 1. Fetch ALL orders in this session (including 'cancel' and 'draft' orders)
        var ordFields = await FilterValidFieldsAsync(config, uid, "pos.order",
            "id", "state", "amount_total", "amount_tax", "amount_paid", "amount_return", "to_invoice", "account_move");
        using var ordDoc = await SearchReadAsync(
            config, uid, "pos.order",
            [new object[] { "session_id", "=", targetSessionId }],
            ordFields);

        if (!ordDoc.RootElement.TryGetProperty("result", out var ordArr) || ordArr.ValueKind != JsonValueKind.Array)
            return;

        var allOrderIds = new List<int>();
        var orderStateMap = new Dictionary<int, (string State, decimal Total, decimal Tax, decimal Paid, decimal Ret, bool ToInv, bool HasMove)>();
        foreach (var oEl in ordArr.EnumerateArray())
        {
            int oid = GetIntOrDefault(oEl, "id");
            if (oid <= 0) continue;
            allOrderIds.Add(oid);
            bool hasMove = oEl.TryGetProperty("account_move", out var mv) && mv.ValueKind == JsonValueKind.Array && mv.GetArrayLength() > 0;
            orderStateMap[oid] = (
                GetStringOrDefault(oEl, "state", "paid"),
                GetDecimalOrDefault(oEl, "amount_total"),
                GetDecimalOrDefault(oEl, "amount_tax"),
                GetDecimalOrDefault(oEl, "amount_paid"),
                GetDecimalOrDefault(oEl, "amount_return"),
                GetBoolOrDefault(oEl, "to_invoice"),
                hasMove
            );
        }

        if (allOrderIds.Count == 0) return;

        // 2. Fetch all lines for these orders
        var lineFields = await FilterValidFieldsAsync(config, uid, "pos.order.line",
            "id", "order_id", "qty", "price_unit", "discount", "price_subtotal", "price_subtotal_incl", "tax_ids");
        using var lineDoc = await SearchReadAsync(
            config, uid, "pos.order.line",
            [new object[] { "order_id", "in", allOrderIds.ToArray() }],
            lineFields);

        var linesByOrder = new Dictionary<int, List<(int LineId, decimal Qty, decimal PriceUnit, decimal Discount, decimal SubExcl, decimal SubIncl, List<int> TaxIds)>>();
        var allTaxIds = new HashSet<int>();
        if (lineDoc.RootElement.TryGetProperty("result", out var lArr) && lArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var lEl in lArr.EnumerateArray())
            {
                int lid = GetIntOrDefault(lEl, "id");
                var (oid, _) = GetMany2One(lEl, "order_id");
                if (lid <= 0 || !oid.HasValue) continue;
                var tIds = GetIdArray(lEl, "tax_ids");
                foreach (int t in tIds) allTaxIds.Add(t);
                if (!linesByOrder.TryGetValue(oid.Value, out var list))
                {
                    list = [];
                    linesByOrder[oid.Value] = list;
                }
                list.Add((
                    lid,
                    GetDecimalOrDefault(lEl, "qty", 1m),
                    GetDecimalOrDefault(lEl, "price_unit"),
                    GetDecimalOrDefault(lEl, "discount"),
                    GetDecimalOrDefault(lEl, "price_subtotal"),
                    GetDecimalOrDefault(lEl, "price_subtotal_incl"),
                    tIds
                ));
            }
        }

        var taxInfoMap = await FetchTaxInfoMapAsync(config, uid, allTaxIds);

        // 3. Fetch all payments in this session
        var paymentsByOrder = new Dictionary<int, List<(int PayId, decimal Amount, int MethodId)>>();
        var orphanPaymentIds = new List<int>();
        using (var pDoc = await SearchReadAsync(config, uid, "pos.payment",
            [new object[] { "session_id", "=", targetSessionId }], ["id", "pos_order_id", "amount", "payment_method_id"]))
        {
            if (pDoc.RootElement.TryGetProperty("result", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var pEl in pArr.EnumerateArray())
                {
                    int pid = GetIntOrDefault(pEl, "id");
                    var (pOid, _) = GetMany2One(pEl, "pos_order_id");
                    var (pmId, _) = GetMany2One(pEl, "payment_method_id");
                    decimal pAmt = GetDecimalOrDefault(pEl, "amount");
                    if (!pOid.HasValue || !orderStateMap.ContainsKey(pOid.Value))
                    {
                        if (pid > 0) orphanPaymentIds.Add(pid);
                        continue;
                    }
                    if (!paymentsByOrder.TryGetValue(pOid.Value, out var pList))
                    {
                        pList = [];
                        paymentsByOrder[pOid.Value] = pList;
                    }
                    pList.Add((pid, pAmt, pmId ?? repairMethodId));
                }
            }
        }

        // 4. Recover orders that were accidentally set to 'cancel' or left in 'draft' despite having lines & payments
        var activeOrderIds = new List<int>();
        foreach (int oid in allOrderIds)
        {
            var ord = orderStateMap[oid];
            bool hasLines = linesByOrder.TryGetValue(oid, out var lns) && lns.Count > 0;
            bool hasPayments = paymentsByOrder.TryGetValue(oid, out var pys) && pys.Sum(p => p.Amount) != 0m;

            if (string.Equals(ord.State, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (hasLines && hasPayments)
                {
                    // This order was accidentally cancelled by the old batch step 4b! Restore it to 'paid'
                    try
                    {
                        using var _ = await ExecuteKwAsync(config, uid, "pos.order", "write",
                            [new[] { oid }, new Dictionary<string, object> { ["state"] = "paid" }]);
                        orderStateMap[oid] = ord with { State = "paid" };
                        activeOrderIds.Add(oid);
                        continue;
                    }
                    catch { }
                }

                // Truly cancelled order: remove any payments attached to it so they don't unbalance the session
                if (paymentsByOrder.TryGetValue(oid, out var cancelPays))
                {
                    foreach (var cp in cancelPays)
                    {
                        orphanPaymentIds.Add(cp.PayId);
                    }
                }
                continue;
            }

            activeOrderIds.Add(oid);
        }

        if (orphanPaymentIds.Count > 0)
        {
            try
            {
                using var _ = await ExecuteKwAsync(config, uid, "pos.payment", "unlink", [orphanPaymentIds.Distinct().ToArray()]);
            }
            catch
            {
                // If unlink is restricted, zero out the orphan payment amounts
                foreach (int opId in orphanPaymentIds.Distinct())
                {
                    try
                    {
                        using var _ = await ExecuteKwAsync(config, uid, "pos.payment", "write",
                            [new[] { opId }, new Dictionary<string, object> { ["amount"] = 0.0 }]);
                    }
                    catch { }
                }
            }
        }

        // 5. Reconcile lines, totals, and payments on all active orders
        foreach (int oid in activeOrderIds)
        {
            var ord = orderStateMap[oid];
            bool canEditOrder = !string.Equals(ord.State, "done", StringComparison.OrdinalIgnoreCase);

            if (canEditOrder && ord.ToInv && !ord.HasMove)
            {
                try
                {
                    using var _ = await ExecuteKwAsync(config, uid, "pos.order", "write",
                        [new[] { oid }, new Dictionary<string, object> { ["to_invoice"] = false }]);
                }
                catch { }
            }

            decimal linesTotalIncl = 0m;
            decimal linesTotalTax = 0m;

            if (linesByOrder.TryGetValue(oid, out var ordLines))
            {
                foreach (var ln in ordLines)
                {
                    var (curExcl, curIncl) = await ComputeOdooLineTaxAmountsAsync(
                        config, uid, ln.TaxIds, taxInfoMap, ln.PriceUnit, ln.Qty, ln.Discount);

                    if (canEditOrder && (Math.Abs(curExcl - ln.SubExcl) >= 0.01m || Math.Abs(curIncl - ln.SubIncl) >= 0.01m))
                    {
                        try
                        {
                            using var _ = await ExecuteKwAsync(config, uid, "pos.order.line", "write",
                                [new[] { ln.LineId }, new Dictionary<string, object>
                                {
                                    ["price_subtotal"] = (double)curExcl,
                                    ["price_subtotal_incl"] = (double)curIncl
                                }]);
                            linesTotalIncl += curIncl;
                            linesTotalTax += (curIncl - curExcl);
                            continue;
                        }
                        catch { }
                    }

                    linesTotalIncl += ln.SubIncl;
                    linesTotalTax += (ln.SubIncl - ln.SubExcl);
                }
            }
            else
            {
                linesTotalIncl = ord.Total;
                linesTotalTax = ord.Tax;
            }

            if (canEditOrder &&
                (Math.Abs(ord.Total - linesTotalIncl) >= 0.01m ||
                 Math.Abs(ord.Tax - linesTotalTax) >= 0.01m ||
                 Math.Abs(ord.Paid - linesTotalIncl) >= 0.01m ||
                 Math.Abs(ord.Ret) >= 0.01m))
            {
                try
                {
                    using var _ = await ExecuteKwAsync(config, uid, "pos.order", "write",
                        [new[] { oid }, new Dictionary<string, object>
                        {
                            ["amount_total"] = (double)linesTotalIncl,
                            ["amount_tax"] = (double)linesTotalTax,
                            ["amount_paid"] = (double)linesTotalIncl,
                            ["amount_return"] = 0.0
                        }]);
                }
                catch { }
            }

            var ordPays = paymentsByOrder.TryGetValue(oid, out var pList) ? pList : [];
            decimal paidSum = ordPays.Sum(p => p.Amount);
            decimal payDiff = OdooRound(linesTotalIncl - paidSum);

            if (Math.Abs(payDiff) >= 0.01m)
            {
                bool updatedExistingPay = false;
                if (ordPays.Count > 0)
                {
                    var lastPay = ordPays[^1];
                    try
                    {
                        using var _ = await ExecuteKwAsync(config, uid, "pos.payment", "write",
                            [new[] { lastPay.PayId }, new Dictionary<string, object>
                            {
                                ["amount"] = (double)OdooRound(lastPay.Amount + payDiff)
                            }]);
                        updatedExistingPay = true;
                    }
                    catch { }
                }

                if (!updatedExistingPay && repairMethodId > 0)
                {
                    try
                    {
                        var adjVals = new Dictionary<string, object>
                        {
                            ["pos_order_id"] = oid,
                            ["session_id"] = targetSessionId,
                            ["payment_method_id"] = repairMethodId,
                            ["amount"] = (double)payDiff,
                            ["payment_date"] = closedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        };
                        using var _ = await ExecuteKwAsync(config, uid, "pos.payment", "create", [adjVals]);
                    }
                    catch { }
                }
            }

            // Ensure any 'draft' order is marked 'paid' (passing singleton [oid])
            if (string.Equals(ord.State, "draft", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var _ = await ExecuteKwAsync(config, uid, "pos.order", "action_pos_order_paid", [new[] { oid }]);
                }
                catch
                {
                    try
                    {
                        using var _ = await ExecuteKwAsync(config, uid, "pos.order", "write",
                            [new[] { oid }, new Dictionary<string, object> { ["state"] = "paid" }]);
                    }
                    catch { }
                }
            }
        }

        // 6. Clear any leftover draft account.move from a prior failed close attempt
        try
        {
            using var sessMoveDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", targetSessionId }],
                ["id", "move_id"]);

            if (sessMoveDoc.RootElement.TryGetProperty("result", out var smArr) &&
                smArr.ValueKind == JsonValueKind.Array &&
                smArr.GetArrayLength() > 0)
            {
                var (moveId, _) = GetMany2One(smArr[0], "move_id");
                if (moveId.HasValue && moveId.Value > 0)
                {
                    using var mvDoc = await SearchReadAsync(
                        config,
                        uid,
                        "account.move",
                        [new object[] { "id", "=", moveId.Value }],
                        ["id", "state"]);

                    if (mvDoc.RootElement.TryGetProperty("result", out var mvArr) &&
                        mvArr.ValueKind == JsonValueKind.Array &&
                        mvArr.GetArrayLength() > 0 &&
                        string.Equals(GetStringOrDefault(mvArr[0], "state"), "draft", StringComparison.OrdinalIgnoreCase))
                    {
                        using var _ = await ExecuteKwAsync(
                            config,
                            uid,
                            "pos.session",
                            "write",
                            [new[] { targetSessionId }, new Dictionary<string, object> { ["move_id"] = false }]);

                        using var __ = await ExecuteKwAsync(
                            config,
                            uid,
                            "account.move",
                            "unlink",
                            [new[] { moveId.Value }]);
                    }
                }
            }
        }
        catch { }
    }

    private async Task PushCloseSessionToOdooAsync(
        OdooConfigRecord config,
        int uid,
        CloseSessionSyncPayload payload,
        int? sessionOdooId)
    {
        var (sessUserId, sessUserName) = await SqliteDatabaseService.Instance.GetSessionUserAsync(payload.LocalSessionId, sessionOdooId ?? payload.SessionOdooId);
        var (execConfig, execUid) = await ResolveExecutionCredentialsAsync(config, uid, sessUserId, sessUserName);

        int targetSessionId = 0;
        if (sessionOdooId.HasValue && sessionOdooId.Value > 0)
        {
            string knownState = await GetOdooSessionStateAsync(execConfig, execUid, sessionOdooId.Value);
            if (!string.IsNullOrWhiteSpace(knownState) &&
                !string.Equals(knownState, "closed", StringComparison.OrdinalIgnoreCase))
            {
                targetSessionId = sessionOdooId.Value;
            }
        }

        if (targetSessionId <= 0)
        {
            using var searchDoc = await SearchReadAsync(
                execConfig,
                execUid,
                "pos.session",
                [
                    new object[] { "config_id", "=", payload.ConfigOdooId },
                    new object[] { "state", "!=", "closed" }
                ],
                ["id", "name", "state"]
            );
            if (searchDoc.RootElement.TryGetProperty("result", out var arr) &&
                arr.ValueKind == JsonValueKind.Array &&
                arr.GetArrayLength() > 0)
            {
                targetSessionId = GetIntOrDefault(arr[0], "id");
            }
        }

        if (targetSessionId <= 0)
        {
            _openSessionByConfigCache.TryRemove(payload.ConfigOdooId, out _);
            return;
        }

        string currentState = await GetOdooSessionStateAsync(execConfig, execUid, targetSessionId);
        if (string.Equals(currentState, "closed", StringComparison.OrdinalIgnoreCase))
        {
            _openSessionByConfigCache.TryRemove(payload.ConfigOdooId, out _);
            return;
        }

        if (string.Equals(currentState, "opening_control", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.session", "set_opening_control", [new[] { targetSessionId }, (double)payload.OpeningBalance, ""]);
            }
            catch
            {
                try
                {
                    using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.session", "action_pos_session_open", [new[] { targetSessionId }]);
                }
                catch { }
            }
        }

        // Write bank_statement_id balances
        try
        {
            using var sessDoc = await SearchReadAsync(
                config,
                uid,
                "pos.session",
                [new object[] { "id", "=", targetSessionId }],
                ["id", "bank_statement_id"]
            );
            if (sessDoc.RootElement.TryGetProperty("result", out var sArr) &&
                sArr.ValueKind == JsonValueKind.Array &&
                sArr.GetArrayLength() > 0)
            {
                var el = sArr[0];
                int stmtId = 0;
                if (el.TryGetProperty("bank_statement_id", out var bVal))
                {
                    if (bVal.ValueKind == JsonValueKind.Array && bVal.GetArrayLength() > 0)
                        stmtId = bVal[0].GetInt32();
                    else if (bVal.ValueKind == JsonValueKind.Number)
                        stmtId = bVal.GetInt32();
                }

                if (stmtId > 0)
                {
                    var stmtVals = new Dictionary<string, object>
                    {
                        ["balance_start"] = (double)payload.OpeningBalance,
                        ["balance_end_real"] = (double)payload.ClosingCashCount
                    };

                    using var _ = await ExecuteKwAsync(
                        config,
                        uid,
                        "account.bank.statement",
                        "write",
                        [new[] { stmtId }, stmtVals]
                    );
                }
            }
        }
        catch { }

        // Update start_at and stop_at on pos.session
        try
        {
            var sessVals = new Dictionary<string, object>
            {
                ["stop_at"] = payload.ClosedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
            if (payload.OpenedAt.HasValue)
            {
                sessVals["start_at"] = payload.OpenedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            using var _ = await ExecuteKwAsync(
                execConfig,
                execUid,
                "pos.session",
                "write",
                [new[] { targetSessionId }, sessVals]
            );
        }
        catch { }

        try
        {
            using var _ = await ExecuteKwAsync(
                execConfig,
                execUid,
                "pos.session",
                "post_closing_cash_details",
                [new[] { targetSessionId }, (double)payload.ClosingCashCount]
            );
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(payload.ClosingNote))
        {
            try
            {
                using var _ = await ExecuteKwAsync(
                    execConfig,
                    execUid,
                    "pos.session",
                    "update_closing_control_state_session",
                    [new[] { targetSessionId }, payload.ClosingNote]
                );
            }
            catch { }
        }

        // Reconcile any draft pos.orders on Odoo ONE BY ONE (singleton) so self.ensure_one() succeeds
        try
        {
            using var draftDoc = await SearchReadAsync(
                execConfig,
                execUid,
                "pos.order",
                [
                    new object[] { "session_id", "=", targetSessionId },
                    new object[] { "state", "=", "draft" }
                ],
                ["id"]
            );
            if (draftDoc.RootElement.TryGetProperty("result", out var dArr) && dArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in dArr.EnumerateArray())
                {
                    int oid = GetIntOrDefault(el, "id");
                    if (oid <= 0) continue;
                    try
                    {
                        using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.order", "action_pos_order_paid", [new[] { oid }]);
                    }
                    catch
                    {
                        try
                        {
                            using var _ = await ExecuteKwAsync(execConfig, execUid, "pos.order", "write",
                                [new[] { oid }, new Dictionary<string, object> { ["state"] = "paid" }]);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        _openSessionByConfigCache.TryRemove(payload.ConfigOdooId, out _);

        // 1. Try fast optimistic close first
        string? lastCloseError = null;
        try
        {
            using var closeDoc = await ExecuteKwAsync(
                execConfig,
                execUid,
                "pos.session",
                "close_session_from_ui",
                [new[] { targetSessionId }, new Dictionary<string, object>()]
            );
            if (closeDoc.RootElement.TryGetProperty("result", out var resEl) &&
                resEl.ValueKind == JsonValueKind.Object &&
                resEl.TryGetProperty("status", out var stEl) && !stEl.GetBoolean())
            {
                string errMsg = resEl.TryGetProperty("message", out var mEl) ? mEl.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(errMsg)) lastCloseError = errMsg;
            }
        }
        catch (Exception ex1)
        {
            lastCloseError = ex1.Message;
        }

        currentState = await GetOdooSessionStateAsync(execConfig, execUid, targetSessionId);

        // 2. If not closed yet (e.g. Session #386 with unbalanced/cancelled orders), run full repair and retry
        if (!string.Equals(currentState, "closed", StringComparison.OrdinalIgnoreCase))
        {
            await RepairSessionOrdersAndPaymentsForCloseAsync(config, uid, targetSessionId, payload.ClosedAt);

            try
            {
                using var closeDoc2 = await ExecuteKwAsync(
                    execConfig,
                    execUid,
                    "pos.session",
                    "close_session_from_ui",
                    [new[] { targetSessionId }, new Dictionary<string, object>()]
                );
                if (closeDoc2.RootElement.TryGetProperty("result", out var resEl2) &&
                    resEl2.ValueKind == JsonValueKind.Object &&
                    resEl2.TryGetProperty("status", out var stEl2) && stEl2.GetBoolean())
                {
                    lastCloseError = null;
                }
            }
            catch (Exception ex2)
            {
                lastCloseError = ex2.Message;
            }

            currentState = await GetOdooSessionStateAsync(execConfig, execUid, targetSessionId);
            if (!string.Equals(currentState, "closed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var _ = await ExecuteKwAsync(
                        execConfig,
                        execUid,
                        "pos.session",
                        "action_pos_session_close",
                        [new[] { targetSessionId }]
                    );
                }
                catch (Exception ex3)
                {
                    lastCloseError = ex3.Message;
                }
                currentState = await GetOdooSessionStateAsync(execConfig, execUid, targetSessionId);
            }
        }

        _sessionPmCache.TryRemove(targetSessionId, out _);

        if (!string.Equals(currentState, "closed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Odoo session #{targetSessionId} remained in state '{currentState}' after close attempt. {lastCloseError}");
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

        var taxMap = new Dictionary<int, (string Label, decimal Rate, bool PriceInclude)>();
        try
        {
            var taxFields = await FilterValidFieldsAsync(config, uid, "account.tax", "id", "name", "amount", "amount_type", "price_include");
            using var taxDoc = await SearchReadAsync(config, uid, "account.tax", [], taxFields);
            if (taxDoc.RootElement.TryGetProperty("result", out var tList) && tList.ValueKind == JsonValueKind.Array)
            {
                foreach (var tItem in tList.EnumerateArray())
                {
                    int tid = GetIntOrDefault(tItem, "id");
                    decimal amt = GetDecimalOrDefault(tItem, "amount", 0m);
                    bool priceIncl = GetBoolOrDefault(tItem, "price_include", false);
                    string tName = GetStringOrDefault(tItem, "name", $"{amt:0.#}%");
                    if (tid > 0) taxMap[tid] = (tName, amt, priceIncl);
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

                string salesTaxLabel = "0%";
                decimal salesTaxRate = 0.0m;
                bool isSalesTaxIncluded = false;
                var salesTaxIds = GetIdArray(item, "taxes_id");
                if (salesTaxIds.Count > 0)
                {
                    var labels = new List<string>();
                    decimal totalRate = 0m;
                    foreach (int tid in salesTaxIds)
                    {
                        if (taxMap.TryGetValue(tid, out var sTax))
                        {
                            labels.Add(sTax.Label);
                            totalRate += sTax.Rate;
                            if (sTax.PriceInclude) isSalesTaxIncluded = true;
                        }
                    }
                    if (labels.Count > 0)
                    {
                        salesTaxLabel = string.Join(", ", labels);
                        salesTaxRate = totalRate;
                    }
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
                        companyName, posDesc, isSalesTaxIncluded
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
            "opening_balance", "closing_balance",
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
                decimal openBal = item.TryGetProperty("opening_balance", out _)
                    ? GetDecimalOrDefault(item, "opening_balance")
                    : GetDecimalOrDefault(item, "cash_register_balance_start");
                decimal closeBal = item.TryGetProperty("closing_balance", out _)
                    ? GetDecimalOrDefault(item, "closing_balance")
                    : GetDecimalOrDefault(item, "cash_register_balance_end_real");
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
        List<PosSessionDto>? preloadedSessions = null,
        List<PosPaymentMethodDto>? preloadedPaymentMethods = null,
        List<PosCategoryDto>? preloadedCategories = null)
    {
        var sessions = preloadedSessions ?? await FetchOdooSessionsInternalAsync(config, uid);
        var sessionsById = sessions.ToDictionary(s => s.OdooId, s => s);
        var sessionsByConfigId = sessions
            .Where(s => s.PosConfigId.HasValue)
            .GroupBy(s => s.PosConfigId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.OdooId).Take(5).Reverse().ToList());

        var payMethodNameById = new Dictionary<int, string>();
        try
        {
            var pms = preloadedPaymentMethods ?? await FetchOdooPaymentMethodsInternalAsync(config, uid);
            foreach (var pm in pms)
            {
                if (pm.OdooId > 0 && !string.IsNullOrWhiteSpace(pm.Name))
                    payMethodNameById[pm.OdooId] = pm.Name.Trim();
            }
        }
        catch { }

        var categoryNameById = new Dictionary<int, string>();
        try
        {
            var cats = preloadedCategories ?? await FetchOdooCategoriesInternalAsync(config, uid);
            foreach (var cat in cats)
            {
                if (cat.OdooId > 0 && !string.IsNullOrWhiteSpace(cat.Name))
                    categoryNameById[cat.OdooId] = cat.Name.Trim();
            }
        }
        catch { }

        var fields = await FilterValidFieldsAsync(config, uid, "pos.config",
            "id", "name", "current_session_id", "current_session_state",
            "last_session_closing_date", "last_session_closing_cash", "pos_session_username",
            "payment_method_ids", "use_fast_payment", "fast_payment_method_ids", "fast_payment_method_id",
            "limit_categories", "iface_available_categ_ids",
            "product_configurator", "group_product_variant", "iface_print_auto");

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

                // 1. Payment Methods (payment_method_ids)
                var pmIds = GetIdArray(item, "payment_method_ids");
                var pmNames = pmIds
                    .Select(id => payMethodNameById.TryGetValue(id, out var n) ? n : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string selectedPaymentMethodsCsv = pmNames.Count > 0
                    ? string.Join(",", pmNames)
                    : "Cash,Card,Customer Account";

                // 2. One-click Payment (use_fast_payment + single method)
                var fastPmIds = GetIdArray(item, "fast_payment_method_ids");
                var (singleFastPmId, singleFastPmName) = GetMany2One(item, "fast_payment_method_id");
                string? oneClickMethodName = null;
                if (fastPmIds.Count > 0 && payMethodNameById.TryGetValue(fastPmIds[0], out var fName))
                {
                    oneClickMethodName = fName;
                }
                else if (!string.IsNullOrWhiteSpace(singleFastPmName))
                {
                    oneClickMethodName = singleFastPmName.Trim();
                }
                else if (singleFastPmId.HasValue && payMethodNameById.TryGetValue(singleFastPmId.Value, out var sName))
                {
                    oneClickMethodName = sName;
                }
                bool isOneClickEnabled = GetBoolOrDefault(item, "use_fast_payment", fastPmIds.Count > 0 || singleFastPmId.HasValue);
                if (string.IsNullOrWhiteSpace(oneClickMethodName))
                {
                    oneClickMethodName = pmNames.FirstOrDefault() ?? "Cash";
                }

                // 3. Restrict Categories (limit_categories + iface_available_categ_ids)
                bool isRestrictCategoriesEnabled = GetBoolOrDefault(item, "limit_categories", false);
                var availCatIds = GetIdArray(item, "iface_available_categ_ids");
                var availCatNames = availCatIds
                    .Select(id => categoryNameById.TryGetValue(id, out var cn) ? cn : null)
                    .Where(cn => !string.IsNullOrWhiteSpace(cn))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string restrictedCategoriesCsv = string.Join(",", availCatNames);

                // 4. Variants (product_configurator / group_product_variant)
                bool isVariantsEnabled = item.TryGetProperty("product_configurator", out _)
                    ? GetBoolOrDefault(item, "product_configurator", true)
                    : GetBoolOrDefault(item, "group_product_variant", true);

                // 5. Automatic Receipt Printing (iface_print_auto)
                bool isAutoPrintEnabled = GetBoolOrDefault(item, "iface_print_auto", false);

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
                        salesJson,
                        selectedPaymentMethodsCsv,
                        isOneClickEnabled,
                        oneClickMethodName!,
                        isRestrictCategoriesEnabled,
                        restrictedCategoriesCsv,
                        isVariantsEnabled,
                        isAutoPrintEnabled
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

        Report(1, "Companies", "Fetching & reconciling Companies (res.company)...");
        var companies = await FetchOdooCompaniesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCompaniesAsync(companies);
        await SqliteDatabaseService.Instance.UpdateCompanySyncStatusAsync(now, $"Companies: {companies.Count}");

        Report(2, "Users", "Fetching & reconciling Users (res.users)...");
        var users = await FetchOdooUsersInternalAsync(config, uid);
        var (uImp, uUpd, uDel) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(users);
        await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(now, $"Users: {uImp} imported, {uUpd} updated, {uDel} removed");

        Report(3, "Payment Methods", "Fetching & reconciling Payment Methods (pos.payment.method)...");
        var paymentMethods = await FetchOdooPaymentMethodsInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooPaymentMethodsAsync(paymentMethods);
        await SqliteDatabaseService.Instance.UpdatePaymentMethodSyncStatusAsync(now, $"Payment Methods: {paymentMethods.Count}");

        Report(4, "PoS Categories", "Fetching & reconciling PoS Categories (pos.category)...");
        var categories = await FetchOdooCategoriesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(categories);
        await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(now, $"Categories: {categories.Count}");

        Report(5, "Attributes", "Fetching & reconciling Product Attributes & Values...");
        var (attrs, attrVals) = await FetchOdooAttributesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(attrs, attrVals);
        await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(now, $"Attributes: {attrs.Count}");

        Report(6, "Product Tags", "Fetching & reconciling Product Tags (product.tag)...");
        var tags = await FetchOdooTagsInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(tags);
        await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(now, $"Tags: {tags.Count}");

        Report(7, "Combo Choices", "Fetching & reconciling Combo Choices...");
        var (combos, comboLines) = await FetchOdooComboChoicesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, comboLines);
        await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(now, $"Combos: {combos.Count}");

        Report(8, "Products", "Fetching & reconciling Products (product.template)...");
        var products = await FetchOdooProductsInternalAsync(config, uid, categories, tags, combos, attrVals);
        await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(products);
        await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(now, $"Products: {products.Count}");

        Report(9, "Sessions", "Fetching & reconciling POS Sessions (pos.session)...");
        var userAvatarMap = users.ToDictionary(u => u.OdooId, u => u.AvatarBase64);
        var sessions = await FetchOdooSessionsInternalAsync(config, uid, userAvatarMap);
        await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessions);
        await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(now, $"Sessions: {sessions.Count}");

        Report(10, "POS Shops", "Fetching & reconciling POS Configurations (pos.config)...");
        var posConfigs = await FetchOdooPosConfigsInternalAsync(config, uid, sessions, paymentMethods, categories);
        await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(posConfigs);
        await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(now, $"POS Shops: {posConfigs.Count}");

        Report(11, "Orders", "Fetching & reconciling POS Orders & Order Lines...");
        var (orders, orderLines, payments) = await FetchOdooOrdersInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, orderLines, payments);
        await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(now, $"Orders: {orders.Count}");

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
