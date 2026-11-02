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
    int UsersCount,
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

public class OdooSyncService
{
    private static readonly Lazy<OdooSyncService> _instance = new(() => new OdooSyncService());
    public static OdooSyncService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, HashSet<string>> _modelFieldsCache = new(StringComparer.OrdinalIgnoreCase);
    private int _rpcIdCounter = 100;

    public bool IsOnline { get; private set; }
    public string ConnectionStatusLabel { get; private set; } = "Offline (Local Mode)";
    public event Action<bool>? OnlineStatusChanged;

    private OdooSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
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

    /// <summary>
    /// Checks live background reachability of the configured Odoo server and raises OnlineStatusChanged if the state changed.
    /// </summary>
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

    /// <summary>
    /// Probes if the host server responds to HTTP requests within a reasonable timeout.
    /// </summary>
    public async Task<bool> CheckServerReachableAsync(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;

        string baseUrl = NormalizeUrl(serverUrl);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var resp = await _httpClient.GetAsync($"{baseUrl}/web/login", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500;
        }
        catch
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var resp2 = await _httpClient.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                return (int)resp2.StatusCode >= 200 && (int)resp2.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Tests connection and authentication with the Odoo instance.
    /// </summary>
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
            if (item.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return s;
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
    // 1. USERS (res.users)
    // =========================================================================
    public async Task<List<OdooUserDto>> FetchOdooUsersAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooUsersInternalAsync(config, uid);
    }

    private async Task<List<OdooUserDto>> FetchOdooUsersInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "res.users",
            "id", "name", "login", "email", "phone", "mobile", "image_1920", "avatar_128", "image_128");

        using var doc = await ExecuteKwAsync(
            config,
            uid,
            "res.users",
            "search_read",
            [new object[] { new object[] { "active", "=", true } }],
            new { fields }
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
                string phone = GetStringOrDefault(item, "phone");
                if (string.IsNullOrWhiteSpace(phone))
                    phone = GetStringOrDefault(item, "mobile");

                string? avatar = GetNullableString(item, "image_1920", "avatar_128", "image_128");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(login))
                {
                    list.Add(new OdooUserDto(odooId, name, login, email, phone, avatar));
                }
            }
        }
        return list;
    }

    // =========================================================================
    // 2. POS CATEGORIES (pos.category)
    // =========================================================================
    public async Task<List<PosCategoryDto>> FetchOdooCategoriesAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooCategoriesInternalAsync(config, uid);
    }

    private async Task<List<PosCategoryDto>> FetchOdooCategoriesInternalAsync(OdooConfigRecord config, int uid)
    {
        // Map pos.config ID -> Name and category links
        var shopMap = new Dictionary<int, string>();
        var categoryToShopsMap = new Dictionary<int, HashSet<string>>();

        try
        {
            var shopFields = await FilterValidFieldsAsync(config, uid, "pos.config",
                "id", "name", "limit_categories", "iface_available_categ_ids");

            using var shopDoc = await ExecuteKwAsync(
                config, uid, "pos.config", "search_read",
                [Array.Empty<object>()],
                new { fields = shopFields }
            );

            if (shopDoc.RootElement.TryGetProperty("result", out var sList) && sList.ValueKind == JsonValueKind.Array)
            {
                foreach (var sItem in sList.EnumerateArray())
                {
                    int sId = GetIntOrDefault(sItem, "id");
                    string sName = GetStringOrDefault(sItem, "name");
                    if (sId > 0 && !string.IsNullOrWhiteSpace(sName))
                    {
                        shopMap[sId] = sName;
                        var catIds = GetIdArray(sItem, "iface_available_categ_ids");
                        foreach (int cId in catIds)
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
        catch { /* Non-fatal if pos.config is not accessible */ }

        var catFields = await FilterValidFieldsAsync(config, uid, "pos.category",
            "id", "name", "parent_id", "sequence", "color", "image_128", "pos_config_ids");

        using var doc = await ExecuteKwAsync(
            config, uid, "pos.category", "search_read",
            [Array.Empty<object>()],
            new { fields = catFields }
        );

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
    // 3. PRODUCT ATTRIBUTES & VALUES (product.attribute & product.attribute.value)
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

        using var attrDoc = await ExecuteKwAsync(
            config, uid, "product.attribute", "search_read",
            [Array.Empty<object>()],
            new { fields = attrFields }
        );

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
                {
                    relatedCount = GetIdArray(item, "product_tmpl_ids").Count;
                }
                var valIds = GetIdArray(item, "value_ids");

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    attributes.Add(new ProductAttributeDto(odooId, name, displayType, createVariant, relatedCount, valIds));
                }
            }
        }

        var valFields = await FilterValidFieldsAsync(config, uid, "product.attribute.value",
            "id", "attribute_id", "name", "is_custom", "html_color", "image", "default_extra_price");

        using var valDoc = await ExecuteKwAsync(
            config, uid, "product.attribute.value", "search_read",
            [Array.Empty<object>()],
            new { fields = valFields }
        );

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
    // 4. PRODUCT TAGS (product.tag)
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

        using var tagDoc = await ExecuteKwAsync(
            config, uid, "product.tag", "search_read",
            [Array.Empty<object>()],
            new { fields = tagFields }
        );

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
                {
                    productsCount = GetIdArray(item, "product_ids").Count;
                }

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    tags.Add(new ProductTagDto(odooId, name, color, productsCount));
                }
            }
        }

        return tags;
    }

    // =========================================================================
    // 5. COMBO CHOICES (product.combo / product.combo.item OR pos.combo / pos.combo.line)
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
        // Determine whether this Odoo version uses product.combo (Odoo 18+) or pos.combo (Odoo 17)
        var productComboFields = await GetAvailableFieldsAsync(config, uid, "product.combo");
        string comboModel = productComboFields.Count > 0 ? "product.combo" : "pos.combo";
        string lineModel = productComboFields.Count > 0 ? "product.combo.item" : "pos.combo.line";

        var combos = new List<ComboChoiceDto>();
        var lines = new List<ComboChoiceLineDto>();

        try
        {
            var cFields = await FilterValidFieldsAsync(config, uid, comboModel,
                "id", "name", "combo_item_ids", "combo_line_ids");

            using var comboDoc = await ExecuteKwAsync(
                config, uid, comboModel, "search_read",
                [Array.Empty<object>()],
                new { fields = cFields }
            );

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

            using var lineDoc = await ExecuteKwAsync(
                config, uid, lineModel, "search_read",
                [Array.Empty<object>()],
                new { fields = lFields }
            );

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

            // Lookup product.product lst_price for combo items
            var prodPriceMap = new Dictionary<int, decimal>();
            if (productIdsToLookupPrice.Count > 0)
            {
                try
                {
                    using var pDoc = await ExecuteKwAsync(
                        config, uid, "product.product", "search_read",
                        [new object[] { new object[] { "id", "in", productIdsToLookupPrice.ToArray() } }],
                        new { fields = new[] { "id", "lst_price" } }
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
        catch
        {
            // Model may not exist if POS module is not installed yet
        }

        return (combos, lines);
    }

    // =========================================================================
    // 6. PRODUCTS (product.template)
    // =========================================================================
    public async Task<List<ProductDto>> FetchOdooProductsAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooProductsInternalAsync(config, uid);
    }

    private async Task<List<ProductDto>> FetchOdooProductsInternalAsync(OdooConfigRecord config, int uid)
    {
        // Build lookup maps for categories, tags, attribute lines, and combos
        var posCategoryMap = new Dictionary<int, string>();
        try
        {
            var cats = await FetchOdooCategoriesInternalAsync(config, uid);
            foreach (var c in cats) posCategoryMap[c.OdooId] = c.Name;
        }
        catch { }

        var tagMap = new Dictionary<int, string>();
        try
        {
            var tags = await FetchOdooTagsInternalAsync(config, uid);
            foreach (var t in tags) tagMap[t.OdooId] = t.Name;
        }
        catch { }

        var comboMap = new Dictionary<int, (string Name, int Count)>();
        try
        {
            var (combos, _) = await FetchOdooComboChoicesInternalAsync(config, uid);
            foreach (var c in combos) comboMap[c.OdooId] = (c.Name, c.LineIds.Count);
        }
        catch { }

        // Map product.template.attribute.line -> AttributeName + Value names
        var templateAttrLinesMap = new Dictionary<int, List<object>>();
        try
        {
            var ptalFields = await FilterValidFieldsAsync(config, uid, "product.template.attribute.line",
                "id", "product_tmpl_id", "attribute_id", "value_ids");

            using var ptalDoc = await ExecuteKwAsync(
                config, uid, "product.template.attribute.line", "search_read",
                [Array.Empty<object>()],
                new { fields = ptalFields }
            );

            // Also map value_ids -> value names
            var (_, attrVals) = await FetchOdooAttributesInternalAsync(config, uid);
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
            "product_variant_count", "image_1920", "image_128", "product_tag_ids",
            "attribute_line_ids", "combo_ids", "available_in_pos");

        using var prodDoc = await ExecuteKwAsync(
            config, uid, "product.template", "search_read",
            [new object[] { new object[] { "active", "=", true } }],
            new { fields = prodFields }
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

                // Resolve POS Category (Odoo 17+ pos_categ_ids Many2many, or Odoo 16 pos_categ_id Many2one)
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

                int variantCount = Math.Max(1, GetIntOrDefault(item, "product_variant_count", 1));
                string? imageBase64 = GetNullableString(item, "image_1920", "image_128");

                // Resolve TagsJson
                string? tagsJson = null;
                var tagIds = GetIdArray(item, "product_tag_ids");
                if (tagIds.Count > 0)
                {
                    var tagNames = tagIds
                        .Select(id => tagMap.TryGetValue(id, out var tn) ? tn : null)
                        .Where(tn => !string.IsNullOrWhiteSpace(tn))
                        .ToList();
                    if (tagNames.Count > 0)
                        tagsJson = JsonSerializer.Serialize(tagNames);
                }

                // Resolve AttributesJson
                string? attributesJson = null;
                if (templateAttrLinesMap.TryGetValue(odooId, out var attrObjs) && attrObjs.Count > 0)
                {
                    attributesJson = JsonSerializer.Serialize(attrObjs);
                }

                // Resolve ComboChoicesJson
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
                        imageBase64, tagsJson, attributesJson, combosJson
                    ));
                }
            }
        }

        return products;
    }

    // =========================================================================
    // 7. POS SESSIONS (pos.session)
    // =========================================================================
    public async Task<List<PosSessionDto>> FetchOdooSessionsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooSessionsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosSessionDto>> FetchOdooSessionsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooSessionsInternalAsync(config, uid);
    }

    private async Task<List<PosSessionDto>> FetchOdooSessionsInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "pos.session",
            "id", "name", "config_id", "user_id", "start_at", "stop_at", "state",
            "cash_register_balance_start", "cash_register_balance_end_real",
            "order_count", "total_payments_amount");

        using var doc = await ExecuteKwAsync(
            config, uid, "pos.session", "search_read",
            [Array.Empty<object>()],
            new { fields }
        );

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

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new PosSessionDto(
                        odooId, name, cfgId, cfgName, userId, userName,
                        null, startAt, stopAt, state, openBal, closeBal,
                        ordersCount, totalPayments, userName
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // 8. POS ORDERS & ORDER LINES (pos.order & pos.order.line)
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
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooOrdersInternalAsync(config, uid);
    }

    private async Task<(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments)> FetchOdooOrdersInternalAsync(
        OdooConfigRecord config,
        int uid)
    {
        var orderFields = await FilterValidFieldsAsync(config, uid, "pos.order",
            "id", "name", "pos_reference", "tracking_number", "session_id", "config_id",
            "date_order", "user_id", "cashier", "fiscal_position_id",
            "amount_total", "amount_tax", "amount_paid", "margin", "margin_percent", "margin_ratio",
            "state", "is_edited", "company_id", "general_note", "note");

        using var doc = await ExecuteKwAsync(
            config, uid, "pos.order", "search_read",
            [Array.Empty<object>()],
            new { fields = orderFields }
        );

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

                if (odooId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    orders.Add(new PosOrderDto(
                        odooId, name, posRef, trackNum, null, sessOdooId,
                        sessName, cfgName, dateOrder, cashierId, cashierName,
                        null, fiscal, amountTotal, amountTax, amountPaid,
                        margin, marginRatio, state, isEdited, company, note, null, null
                    ));
                }
            }
        }

        // Fetch pos.order.line records
        var lines = new List<PosOrderLineDto>();
        try
        {
            var lineFields = await FilterValidFieldsAsync(config, uid, "pos.order.line",
                "id", "order_id", "product_id", "full_product_name", "name",
                "qty", "price_unit", "discount", "price_subtotal", "price_subtotal_incl");

            using var lineDoc = await ExecuteKwAsync(
                config, uid, "pos.order.line", "search_read",
                [Array.Empty<object>()],
                new { fields = lineFields }
            );

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

                    string taxLabel = subTotal > 0 && subTotalIncl > subTotal
                        ? $"{Math.Round(((subTotalIncl - subTotal) / subTotal) * 100m, 0)}%"
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

        // Fetch pos.payment records
        var payments = await FetchOdooPaymentsInternalAsync(config, uid);

        return (orders, lines, payments);
    }

    // =========================================================================
    // 9. POS PAYMENTS (pos.payment)
    // =========================================================================
    public async Task<List<PosPaymentDto>> FetchOdooPaymentsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooPaymentsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosPaymentDto>> FetchOdooPaymentsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        int uid = await EnsureAuthenticatedUidAsync(config);
        return await FetchOdooPaymentsInternalAsync(config, uid);
    }

    private async Task<List<PosPaymentDto>> FetchOdooPaymentsInternalAsync(OdooConfigRecord config, int uid)
    {
        var fields = await FilterValidFieldsAsync(config, uid, "pos.payment",
            "id", "pos_order_id", "session_id", "payment_method_id", "amount", "payment_date", "currency_id", "cashier");

        using var doc = await ExecuteKwAsync(
            config, uid, "pos.payment", "search_read",
            [Array.Empty<object>()],
            new { fields }
        );

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
                string cashier = GetStringOrDefault(item, "cashier", "Cashier");

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
    // 10. POS CONFIGURATIONS (pos.config)
    // =========================================================================
    public async Task<List<PosConfigDto>> FetchOdooPosConfigsAsync(bool allowOfflineSimulation = false)
    {
        var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        return await FetchOdooPosConfigsAsync(config, allowOfflineSimulation);
    }

    public async Task<List<PosConfigDto>> FetchOdooPosConfigsAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
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

        using var doc = await ExecuteKwAsync(
            config, uid, "pos.config", "search_read",
            [Array.Empty<object>()],
            new { fields }
        );

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

                if (sessId.HasValue && sessionsById.TryGetValue(sessId.Value, out var activeSess))
                {
                    openingBal = activeSess.OpeningBalance;
                    activeUserId = activeSess.UserId;
                    if (string.IsNullOrWhiteSpace(activeUserName))
                        activeUserName = activeSess.UserName;
                    if (activeSess.StartAt.HasValue && !lastCloseDate.HasValue)
                        lastCloseDate = activeSess.StartAt;
                }

                // Build sparkline from recent sessions of this POS config
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
                        null,
                        salesJson
                    ));
                }
            }
        }

        return list;
    }

    // =========================================================================
    // MASTER SYNCHRONIZATION PIPELINE (ALL 10 ENTITIES IN DEPENDENCY ORDER)
    // =========================================================================
    public async Task<SyncAllSummary> SyncAllFromOdooAsync(
        OdooConfigRecord? overrideConfig = null,
        IProgress<SyncProgressReport>? progress = null)
    {
        var config = overrideConfig ?? await SqliteDatabaseService.Instance.GetOdooConfigAsync();
        const int totalSteps = 10;

        void Report(int step, string module, string message)
        {
            double pct = Math.Round((double)step / totalSteps * 100.0, 0);
            progress?.Report(new SyncProgressReport(step, totalSteps, module, message, pct));
        }

        Report(0, "Authentication", "Authenticating with Odoo server...");
        int uid = await EnsureAuthenticatedUidAsync(config);
        _modelFieldsCache.Clear();

        DateTime now = DateTime.Now;

        // 1. Users
        Report(1, "Users", "Fetching & reconciling Users (res.users)...");
        var users = await FetchOdooUsersInternalAsync(config, uid);
        var (uImp, uUpd, uDel) = await SqliteDatabaseService.Instance.UpsertOdooUsersAsync(users);
        await SqliteDatabaseService.Instance.UpdateSyncStatusAsync(now, $"Users: {uImp} imported, {uUpd} updated, {uDel} removed");

        // 2. POS Categories
        Report(2, "PoS Categories", "Fetching & reconciling PoS Categories (pos.category)...");
        var categories = await FetchOdooCategoriesInternalAsync(config, uid);
        var (cImp, cUpd, cDel) = await SqliteDatabaseService.Instance.UpsertOdooCategoriesAsync(categories);
        await SqliteDatabaseService.Instance.UpdateCategorySyncStatusAsync(now, $"Categories: {cImp} imported, {cUpd} updated, {cDel} removed");

        // 3. Product Attributes & Values
        Report(3, "Attributes", "Fetching & reconciling Product Attributes & Values...");
        var (attrs, attrVals) = await FetchOdooAttributesInternalAsync(config, uid);
        await SqliteDatabaseService.Instance.UpsertOdooAttributesAsync(attrs, attrVals);
        await SqliteDatabaseService.Instance.UpdateAttributeSyncStatusAsync(now, $"Attributes: {attrs.Count} attributes, {attrVals.Count} values");

        // 4. Product Tags
        Report(4, "Product Tags", "Fetching & reconciling Product Tags (product.tag)...");
        var tags = await FetchOdooTagsInternalAsync(config, uid);
        var (tImp, tUpd, tDel) = await SqliteDatabaseService.Instance.UpsertOdooTagsAsync(tags);
        await SqliteDatabaseService.Instance.UpdateTagSyncStatusAsync(now, $"Tags: {tImp} imported, {tUpd} updated, {tDel} removed");

        // 5. Combo Choices
        Report(5, "Combo Choices", "Fetching & reconciling Combo Choices...");
        var (combos, comboLines) = await FetchOdooComboChoicesInternalAsync(config, uid);
        var (cbImp, cbUpd, cbDel) = await SqliteDatabaseService.Instance.UpsertOdooCombosAsync(combos, comboLines);
        await SqliteDatabaseService.Instance.UpdateComboSyncStatusAsync(now, $"Combos: {cbImp} imported, {cbUpd} updated, {cbDel} removed");

        // 6. Products
        Report(6, "Products", "Fetching & reconciling Products (product.template)...");
        var products = await FetchOdooProductsInternalAsync(config, uid);
        var (pImp, pUpd, pDel) = await SqliteDatabaseService.Instance.UpsertOdooProductsAsync(products);
        await SqliteDatabaseService.Instance.UpdateProductSyncStatusAsync(now, $"Products: {pImp} imported, {pUpd} updated, {pDel} removed");

        // 7. POS Sessions
        Report(7, "Sessions", "Fetching & reconciling POS Sessions (pos.session)...");
        var sessions = await FetchOdooSessionsInternalAsync(config, uid);
        var (sImp, sUpd, sDel) = await SqliteDatabaseService.Instance.UpsertOdooSessionsAsync(sessions);
        await SqliteDatabaseService.Instance.UpdateSessionSyncStatusAsync(now, $"Sessions: {sImp} imported, {sUpd} updated, {sDel} removed");

        // 8. POS Configurations (uses sessions for sparkline & active balance)
        Report(8, "POS Shops", "Fetching & reconciling POS Configurations (pos.config)...");
        var posConfigs = await FetchOdooPosConfigsInternalAsync(config, uid, sessions);
        var (cfgImp, cfgUpd, cfgDel) = await SqliteDatabaseService.Instance.UpsertOdooPosConfigsAsync(posConfigs);
        await SqliteDatabaseService.Instance.UpdatePosConfigSyncStatusAsync(now, $"POS Shops: {cfgImp} imported, {cfgUpd} updated, {cfgDel} removed");

        // 9. POS Orders & Order Lines
        Report(9, "Orders", "Fetching & reconciling POS Orders & Order Lines...");
        var (orders, orderLines, payments) = await FetchOdooOrdersInternalAsync(config, uid);
        var (oImp, oUpd, oDel) = await SqliteDatabaseService.Instance.UpsertOdooOrdersAsync(orders, orderLines, payments);
        await SqliteDatabaseService.Instance.UpdateOrderSyncStatusAsync(now, $"Orders: {oImp} imported, {oUpd} updated, {oDel} removed");

        // 10. POS Payments
        Report(10, "Payments", "Reconciling POS Payments (pos.payment)...");
        var (payImp, payUpd, payDel) = await SqliteDatabaseService.Instance.UpsertOdooPaymentsAsync(payments);
        await SqliteDatabaseService.Instance.UpdatePaymentSyncStatusAsync(now, $"Payments: {payImp} imported, {payUpd} updated, {payDel} removed");

        string summaryMsg = $"Synchronized {posConfigs.Count} shops, {products.Count} products, {categories.Count} categories, {orders.Count} orders, {users.Count} users.";
        return new SyncAllSummary(
            true,
            summaryMsg,
            users.Count,
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