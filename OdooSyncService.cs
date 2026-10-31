using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OdooPosLogin;

public record ConnectionTestResult(bool Success, string Message, int? Uid = null);

public class OdooSyncService
{
    private static readonly Lazy<OdooSyncService> _instance = new(() => new OdooSyncService());
    public static OdooSyncService Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    public bool IsOnline { get; private set; } = false;
    public string ConnectionStatusLabel { get; private set; } = "Offline (Local Mode)";
    public event Action<bool>? OnlineStatusChanged;

    private OdooSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "https://localhost";
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
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
            var resp = await _httpClient.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500;
        }
        catch
        {
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var req = new HttpRequestMessage(HttpMethod.Head, baseUrl);
                var resp2 = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
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

        try
        {
            // Standard Odoo JSON-RPC /jsonrpc endpoint
            string endpoint = $"{baseUrl}/jsonrpc";
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
                        config.DatabaseName,
                        config.Username,
                        config.ApiKey ?? "",
                        new { }
                    }
                },
                id = 1
            };

            var jsonContent = new StringContent(
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
                        return new ConnectionTestResult(true, $"Connected: Odoo instance verified (UID: {uid})", uid);
                    }
                    if (resultProp.ValueKind == JsonValueKind.False)
                    {
                        UpdateStatus(true, "Odoo Online"); // Host is online even if credentials failed
                        return new ConnectionTestResult(false, "Authentication Failed: Invalid Database, Username, or API Key.");
                    }
                }

                if (root.TryGetProperty("error", out var errorProp))
                {
                    UpdateStatus(true, "Odoo Online");
                    string errMsg = "Odoo Error";
                    if (errorProp.TryGetProperty("data", out var dataProp) &&
                        dataProp.TryGetProperty("message", out var msgProp))
                    {
                        errMsg = msgProp.GetString() ?? errMsg;
                    }
                    else if (errorProp.TryGetProperty("message", out var mProp))
                    {
                        errMsg = mProp.GetString() ?? errMsg;
                    }
                    return new ConnectionTestResult(false, $"Connection Error: {errMsg}");
                }
            }

            UpdateStatus(true, "Odoo Online");
            return new ConnectionTestResult(false, $"Server returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, $"Cannot reach server: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, "Connection timed out (10s). Verify host and network.");
        }
        catch (Exception ex)
        {
            UpdateStatus(false, "Offline (Local Mode)");
            return new ConnectionTestResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches users from Odoo's res.users model via JSON-RPC.
    /// If simulated fallback is allowed and server is online, fallback mock users are provided for demo.
    /// </summary>
    public async Task<List<OdooUserDto>> FetchOdooUsersAsync(OdooConfigRecord config, bool allowOfflineSimulation = true)
    {
        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        try
        {
            // 1. Authenticate to get UID
            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation)
                {
                    // Sample Odoo users for demo testing
                    return GetSimulatedOdooUsers();
                }
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            // 2. Fetch res.users via execute_kw
            var queryPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                        config.DatabaseName,
                        uid,
                        config.ApiKey ?? "",
                        "res.users",
                        "search_read",
                        new object[] { new object[] { new object[] { "active", "=", true } } },
                        new
                        {
                            fields = new[] { "id", "name", "login", "email", "phone", "image_1920", "avatar_128" }
                        }
                    }
                },
                id = 2
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(queryPayload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(endpoint, jsonContent);
            response.EnsureSuccessStatusCode();

            string respString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respString);

            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                string errMsg = "Odoo Error";
                if (errorProp.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("message", out var msgProp))
                {
                    errMsg = msgProp.GetString() ?? errMsg;
                }
                else if (errorProp.TryGetProperty("message", out var mProp))
                {
                    errMsg = mProp.GetString() ?? errMsg;
                }
                throw new InvalidOperationException($"Odoo query failed: {errMsg}");
            }

            if (doc.RootElement.TryGetProperty("result", out var resultList) &&
                resultList.ValueKind == JsonValueKind.Array)
            {
                var list = new List<OdooUserDto>();
                foreach (var item in resultList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    string login = item.TryGetProperty("login", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
                    string email = item.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                    string phone = item.TryGetProperty("phone", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

                    string? avatar = null;
                    if (item.TryGetProperty("image_1920", out var img) && img.ValueKind == JsonValueKind.String)
                        avatar = img.GetString();
                    else if (item.TryGetProperty("avatar_128", out var av) && av.ValueKind == JsonValueKind.String)
                        avatar = av.GetString();

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(login))
                    {
                        list.Add(new OdooUserDto(odooId, name, login, email, phone, avatar));
                    }
                }
                UpdateStatus(true, "Odoo Online");
                return list;
            }

            if (allowOfflineSimulation)
                return GetSimulatedOdooUsers();

            return [];
        }
        catch
        {
            if (allowOfflineSimulation)
            {
                return GetSimulatedOdooUsers();
            }
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    /// <summary>
    /// Provides simulated Odoo 20 users for demo environments.
    /// </summary>
    public static List<OdooUserDto> GetSimulatedOdooUsers()
    {
        return
        [
            new OdooUserDto(
                1,
                "Administrator",
                "admin",
                "admin@odoo20-pos.example.com",
                "+1 555-0100",
                null
            ),
            new OdooUserDto(
                2,
                "Marc Demo",
                "demo",
                "marc.demo@odoo20-pos.example.com",
                "+1 555-0144",
                null
            ),
            new OdooUserDto(
                3,
                "Mitchell Admin",
                "mitchell",
                "mitchell.admin@odoo20-pos.example.com",
                "+1 555-0182",
                null
            ),
            new OdooUserDto(
                14,
                "Sarah Connor",
                "sconnor",
                "sarah.connor@odoo20-pos.example.com",
                "+1 555-0199",
                null
            ),
            new OdooUserDto(
                22,
                "Alexandre Moreau",
                "amoreau",
                "alex.moreau@odoo20-pos.example.com",
                "+33 1 42 68 00 11",
                null
            ),
            new OdooUserDto(
                35,
                "Elena Rostova",
                "erostova",
                "elena.rostova@odoo20-pos.example.com",
                "+44 20 7946 0912",
                null
            )
        ];
    }

    /// <summary>
    /// Fetches POS product categories from Odoo's pos.category model via JSON-RPC.
    /// Also queries pos.config to resolve human-readable shop names for linked POS configs.
    /// </summary>
    public async Task<List<PosCategoryDto>> FetchOdooCategoriesAsync(OdooConfigRecord config, bool allowOfflineSimulation = false)
    {
        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        try
        {
            // 1. Authenticate to get UID
            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation)
                {
                    return GetSimulatedOdooCategories();
                }
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            // 2. Query pos.config to map ID -> Shop Name
            var shopMap = new Dictionary<int, string>();
            try
            {
                var shopPayload = new
                {
                    jsonrpc = "2.0",
                    method = "call",
                    @params = new
                    {
                        service = "object",
                        method = "execute_kw",
                        args = new object[]
                        {
                            config.DatabaseName,
                            uid,
                            config.ApiKey ?? "",
                            "pos.config",
                            "search_read",
                            new object[] { new object[] { } },
                            new { fields = new[] { "id", "name" } }
                        }
                    },
                    id = 11
                };

                var shopReq = new StringContent(JsonSerializer.Serialize(shopPayload), Encoding.UTF8, "application/json");
                var shopResp = await _httpClient.PostAsync(endpoint, shopReq);
                if (shopResp.IsSuccessStatusCode)
                {
                    string s = await shopResp.Content.ReadAsStringAsync();
                    using var sDoc = JsonDocument.Parse(s);
                    if (sDoc.RootElement.TryGetProperty("result", out var sList) && sList.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sItem in sList.EnumerateArray())
                        {
                            int sId = sItem.GetProperty("id").GetInt32();
                            string sName = sItem.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                            shopMap[sId] = sName;
                        }
                    }
                }
            }
            catch { /* Fallback to ID numbers if pos.config query fails */ }

            // 3. Query pos.category records
            var categoryPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                        config.DatabaseName,
                        uid,
                        config.ApiKey ?? "",
                        "pos.category",
                        "search_read",
                        new object[] { new object[] { } },
                        new
                        {
                            fields = new[] { "id", "name", "parent_id", "sequence", "color", "image_128", "pos_config_ids" }
                        }
                    }
                },
                id = 12
            };

            var catReq = new StringContent(JsonSerializer.Serialize(categoryPayload), Encoding.UTF8, "application/json");
            var catResp = await _httpClient.PostAsync(endpoint, catReq);
            catResp.EnsureSuccessStatusCode();

            string respString = await catResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respString);

            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                string errMsg = "Odoo Error";
                if (errorProp.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("message", out var msgProp))
                {
                    errMsg = msgProp.GetString() ?? errMsg;
                }
                else if (errorProp.TryGetProperty("message", out var mProp))
                {
                    errMsg = mProp.GetString() ?? errMsg;
                }
                throw new InvalidOperationException($"Odoo categories query failed: {errMsg}");
            }

            if (doc.RootElement.TryGetProperty("result", out var resultList) &&
                resultList.ValueKind == JsonValueKind.Array)
            {
                var list = new List<PosCategoryDto>();
                foreach (var item in resultList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";

                    // Parent category: false or [id, "Parent Name"]
                    int? parentId = null;
                    string? parentName = null;
                    if (item.TryGetProperty("parent_id", out var pProp) && pProp.ValueKind == JsonValueKind.Array && pProp.GetArrayLength() >= 2)
                    {
                        parentId = pProp[0].GetInt32();
                        parentName = pProp[1].GetString();
                    }

                    int sequence = item.TryGetProperty("sequence", out var sProp) && sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32() : 10;
                    int color = item.TryGetProperty("color", out var cProp) && cProp.ValueKind == JsonValueKind.Number ? cProp.GetInt32() : 0;
                    string colorHex = PosCategoryRecord.GetOdooColorHex(color);

                    string? imageBase64 = null;
                    if (item.TryGetProperty("image_128", out var imgProp) && imgProp.ValueKind == JsonValueKind.String)
                    {
                        imageBase64 = imgProp.GetString();
                    }

                    // Linked POS config shops: [1, 2]
                    var shops = new List<string>();
                    if (item.TryGetProperty("pos_config_ids", out var configsProp) && configsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cId in configsProp.EnumerateArray())
                        {
                            int id = cId.GetInt32();
                            if (shopMap.TryGetValue(id, out var sName))
                                shops.Add(sName);
                            else
                                shops.Add($"POS {id}");
                        }
                    }
                    string linkedShops = string.Join(", ", shops);

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(new PosCategoryDto(odooId, name, parentId, parentName, linkedShops, color, colorHex, sequence, imageBase64));
                    }
                }

                UpdateStatus(true, "Odoo Online");
                return list;
            }

            if (allowOfflineSimulation)
                return GetSimulatedOdooCategories();

            return [];
        }
        catch
        {
            if (allowOfflineSimulation)
            {
                return GetSimulatedOdooCategories();
            }
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    /// <summary>
    /// Provides simulated Odoo 20 POS categories for demo / offline environments.
    /// </summary>
    public static List<PosCategoryDto> GetSimulatedOdooCategories()
    {
        return
        [
            new PosCategoryDto(1, "Misc", null, null, "Furniture Shop", 9, PosCategoryRecord.GetOdooColorHex(9), 1, null),
            new PosCategoryDto(2, "Desks", null, null, "Furniture Shop", 3, PosCategoryRecord.GetOdooColorHex(3), 2, null),
            new PosCategoryDto(3, "Chairs", null, null, "Furniture Shop", 1, PosCategoryRecord.GetOdooColorHex(1), 3, null),
            new PosCategoryDto(4, "Upper body", null, null, "Clothes Shop", 5, PosCategoryRecord.GetOdooColorHex(5), 4, null),
            new PosCategoryDto(5, "Lower body", null, null, "Clothes Shop", 10, PosCategoryRecord.GetOdooColorHex(10), 5, null),
            new PosCategoryDto(6, "Others", null, null, "Clothes Shop", 3, PosCategoryRecord.GetOdooColorHex(3), 6, null),
            new PosCategoryDto(7, "Breads", null, null, "Bakery Shop", 7, PosCategoryRecord.GetOdooColorHex(7), 7, null),
            new PosCategoryDto(8, "Pastries", null, null, "Bakery Shop", 8, PosCategoryRecord.GetOdooColorHex(8), 8, null)
        ];
    }

    /// <summary>
    /// Fetches product attributes and their values from Odoo Online (product.attribute & product.attribute.value)
    /// </summary>
    public async Task<(List<ProductAttributeDto> attributes, List<ProductAttributeValueDto> values)> FetchOdooAttributesAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        try
        {
            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation)
                {
                    return GetSimulatedOdooAttributes();
                }
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            // 1. Fetch product.attribute records
            var attrPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                        config.DatabaseName,
                        uid,
                        config.ApiKey ?? "",
                        "product.attribute",
                        "search_read",
                        new object[] { new object[] { } },
                        new
                        {
                            fields = new[] { "id", "name", "display_type", "create_variant", "number_related_products", "value_ids" }
                        }
                    }
                },
                id = 21
            };

            var attrReq = new StringContent(JsonSerializer.Serialize(attrPayload), Encoding.UTF8, "application/json");
            var attrResp = await _httpClient.PostAsync(endpoint, attrReq);
            attrResp.EnsureSuccessStatusCode();

            string attrStr = await attrResp.Content.ReadAsStringAsync();
            using var attrDoc = JsonDocument.Parse(attrStr);

            if (attrDoc.RootElement.TryGetProperty("error", out var err1))
            {
                string msg = err1.TryGetProperty("data", out var d1) && d1.TryGetProperty("message", out var m1)
                    ? m1.GetString() ?? "Odoo RPC Error"
                    : err1.GetProperty("message").GetString() ?? "Odoo RPC Error";
                throw new InvalidOperationException($"product.attribute RPC error: {msg}");
            }

            var attributes = new List<ProductAttributeDto>();
            if (attrDoc.RootElement.TryGetProperty("result", out var attrList) && attrList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in attrList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    string displayType = item.TryGetProperty("display_type", out var dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString() ?? "radio" : "radio";
                    string createVariant = item.TryGetProperty("create_variant", out var cv) && cv.ValueKind == JsonValueKind.String ? cv.GetString() ?? "always" : "always";
                    int relatedCount = item.TryGetProperty("number_related_products", out var nr) && nr.ValueKind == JsonValueKind.Number ? nr.GetInt32() : 0;

                    var valIds = new List<int>();
                    if (item.TryGetProperty("value_ids", out var vids) && vids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var vid in vids.EnumerateArray())
                        {
                            if (vid.ValueKind == JsonValueKind.Number) valIds.Add(vid.GetInt32());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        attributes.Add(new ProductAttributeDto(odooId, name, displayType, createVariant, relatedCount, valIds));
                    }
                }
            }

            // 2. Fetch product.attribute.value records
            var valPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                        config.DatabaseName,
                        uid,
                        config.ApiKey ?? "",
                        "product.attribute.value",
                        "search_read",
                        new object[] { new object[] { } },
                        new
                        {
                            fields = new[] { "id", "attribute_id", "name", "is_custom", "html_color", "image", "default_extra_price" }
                        }
                    }
                },
                id = 22
            };

            var valReq = new StringContent(JsonSerializer.Serialize(valPayload), Encoding.UTF8, "application/json");
            var valResp = await _httpClient.PostAsync(endpoint, valReq);
            valResp.EnsureSuccessStatusCode();

            string valStr = await valResp.Content.ReadAsStringAsync();
            using var valDoc = JsonDocument.Parse(valStr);

            var values = new List<ProductAttributeValueDto>();
            if (valDoc.RootElement.TryGetProperty("result", out var valList) && valList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";

                    int attrId = 0;
                    if (item.TryGetProperty("attribute_id", out var aProp))
                    {
                        if (aProp.ValueKind == JsonValueKind.Array && aProp.GetArrayLength() > 0)
                            attrId = aProp[0].GetInt32();
                        else if (aProp.ValueKind == JsonValueKind.Number)
                            attrId = aProp.GetInt32();
                    }

                    bool isCustom = item.TryGetProperty("is_custom", out var ic) && (ic.ValueKind == JsonValueKind.True || (ic.ValueKind == JsonValueKind.Number && ic.GetInt32() == 1));

                    string? htmlColor = null;
                    if (item.TryGetProperty("html_color", out var hc) && hc.ValueKind == JsonValueKind.String)
                    {
                        htmlColor = hc.GetString();
                    }

                    string? imageBase64 = null;
                    if (item.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
                    {
                        imageBase64 = img.GetString();
                    }

                    double extraPrice = 0.0;
                    if (item.TryGetProperty("default_extra_price", out var ep) && ep.ValueKind == JsonValueKind.Number)
                    {
                        extraPrice = ep.GetDouble();
                    }

                    if (!string.IsNullOrWhiteSpace(name) && attrId > 0)
                    {
                        values.Add(new ProductAttributeValueDto(odooId, attrId, name, isCustom, htmlColor, imageBase64, extraPrice));
                    }
                }
            }

            UpdateStatus(true, "Odoo Online");
            return (attributes, values);
        }
        catch
        {
            if (allowOfflineSimulation)
            {
                return GetSimulatedOdooAttributes();
            }
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    /// <summary>
    /// Fetches product tags from Odoo Online (product.tag)
    /// </summary>
    public async Task<List<ProductTagDto>> FetchOdooTagsAsync(
        OdooConfigRecord config,
        bool allowOfflineSimulation = false)
    {
        string baseUrl = NormalizeUrl(config.ServerUrl);
        string endpoint = $"{baseUrl}/jsonrpc";

        try
        {
            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation)
                {
                    return GetSimulatedOdooTags();
                }
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var tagPayload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    service = "object",
                    method = "execute_kw",
                    args = new object[]
                    {
                        config.DatabaseName,
                        uid,
                        config.ApiKey ?? "",
                        "product.tag",
                        "search_read",
                        new object[] { new object[] { } },
                        new
                        {
                            fields = new[] { "id", "name", "color", "product_template_ids" }
                        }
                    }
                },
                id = 23
            };

            var tagReq = new StringContent(JsonSerializer.Serialize(tagPayload), Encoding.UTF8, "application/json");
            var tagResp = await _httpClient.PostAsync(endpoint, tagReq);
            tagResp.EnsureSuccessStatusCode();

            string tagStr = await tagResp.Content.ReadAsStringAsync();
            using var tagDoc = JsonDocument.Parse(tagStr);

            var tags = new List<ProductTagDto>();
            if (tagDoc.RootElement.TryGetProperty("result", out var tagList) && tagList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    int color = item.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;

                    int productsCount = 0;
                    if (item.TryGetProperty("product_template_ids", out var pt) && pt.ValueKind == JsonValueKind.Array)
                    {
                        productsCount = pt.GetArrayLength();
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tags.Add(new ProductTagDto(odooId, name, color, productsCount));
                    }
                }
            }

            UpdateStatus(true, "Odoo Online");
            return tags;
        }
        catch
        {
            if (allowOfflineSimulation)
            {
                return GetSimulatedOdooTags();
            }
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    public static (List<ProductAttributeDto>, List<ProductAttributeValueDto>) GetSimulatedOdooAttributes()
    {
        var attrs = new List<ProductAttributeDto>
        {
            new(1, "Brand", "radio", "always", 0, [1, 2, 3, 4, 5, 6]),
            new(2, "Size", "pills", "always", 5, [41, 42, 43, 44, 45]),
            new(3, "Color", "color", "always", 3, [50, 51, 52, 53, 54]),
            new(4, "Material", "radio", "always", 0, [70, 71, 72]),
            new(13, "Shoes size", "pills", "no_variant", 2, [97, 98, 99, 100, 101])
        };

        var vals = new List<ProductAttributeValueDto>
        {
            new(1, 1, "Adidas", false, null, null, 0.0),
            new(2, 1, "Apple", false, null, null, 0.0),
            new(3, 1, "Ariel", false, null, null, 0.0),
            new(4, 1, "Canon", false, null, null, 0.0),
            new(5, 1, "Coca-Cola", false, null, null, 0.0),
            new(6, 1, "Nike", false, null, null, 0.0),
            new(41, 2, "XS", false, null, null, 0.0),
            new(42, 2, "S", false, null, null, 0.0),
            new(43, 2, "M", false, null, null, 0.0),
            new(44, 2, "L", false, null, null, 0.0),
            new(45, 2, "XL", false, null, null, 0.0),
            new(50, 3, "White", false, "#FFFFFF", null, 0.0),
            new(51, 3, "Black", false, "#000000", null, 0.0),
            new(52, 3, "Red", false, "#EF4444", null, 2.50),
            new(53, 3, "Blue", false, "#3B82F6", null, 2.50),
            new(54, 3, "Green", false, "#10B981", null, 0.0),
            new(70, 4, "Cotton", false, null, null, 0.0),
            new(71, 4, "Leather", false, null, null, 15.0),
            new(72, 4, "Wood", false, null, null, 0.0),
            new(97, 13, "38", false, null, null, 0.0),
            new(98, 13, "39", false, null, null, 0.0),
            new(99, 13, "40", false, null, null, 0.0),
            new(100, 13, "41", false, null, null, 0.0),
            new(101, 13, "42", false, null, null, 0.0)
        };

        return (attrs, vals);
    }

    public static List<ProductTagDto> GetSimulatedOdooTags()
    {
        return
        [
            new(1, "Bestseller", 1, 12),
            new(2, "New Arrival", 10, 8),
            new(3, "Organic", 7, 5),
            new(4, "Featured", 5, 4),
            new(5, "Special Offer", 2, 9)
        ];
    }
}
