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

    /// <summary>
    /// Fetches products from Odoo Online (product.template)
    /// </summary>
    public async Task<List<ProductDto>> FetchOdooProductsAsync(
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
                if (allowOfflineSimulation) return GetSimulatedOdooProducts();
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var productPayload = new
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
                        "product.template",
                        "search_read",
                        new object[] { new object[] { } },
                        new
                        {
                            fields = new[]
                            {
                                "id", "name", "type", "list_price", "standard_price",
                                "default_code", "barcode", "pos_categ_id", "product_variant_count",
                                "image_1920", "product_tag_ids"
                            }
                        }
                    }
                },
                id = 45
            };

            var prodReq = new StringContent(JsonSerializer.Serialize(productPayload), Encoding.UTF8, "application/json");
            var prodResp = await _httpClient.PostAsync(endpoint, prodReq);
            prodResp.EnsureSuccessStatusCode();

            string prodStr = await prodResp.Content.ReadAsStringAsync();
            using var prodDoc = JsonDocument.Parse(prodStr);

            var products = new List<ProductDto>();
            if (prodDoc.RootElement.TryGetProperty("result", out var prodList) && prodList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prodList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    string type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "consu" : "consu";
                    decimal listPrice = item.TryGetProperty("list_price", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetDecimal() : 0.00m;
                    decimal stdPrice = item.TryGetProperty("standard_price", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetDecimal() : 0.00m;
                    string sku = item.TryGetProperty("default_code", out var dc) && dc.ValueKind == JsonValueKind.String ? dc.GetString() ?? "" : "";
                    string barcode = item.TryGetProperty("barcode", out var bc) && bc.ValueKind == JsonValueKind.String ? bc.GetString() ?? "" : "";

                    int? catId = null;
                    string? catName = null;
                    if (item.TryGetProperty("pos_categ_id", out var pc) && pc.ValueKind == JsonValueKind.Array && pc.GetArrayLength() >= 2)
                    {
                        catId = pc[0].GetInt32();
                        catName = pc[1].GetString();
                    }

                    int variantCount = item.TryGetProperty("product_variant_count", out var vc) && vc.ValueKind == JsonValueKind.Number ? vc.GetInt32() : 1;
                    if (variantCount < 1) variantCount = 1;

                    string? imageBase64 = null;
                    if (item.TryGetProperty("image_1920", out var img) && img.ValueKind == JsonValueKind.String)
                    {
                        string s = img.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase))
                        {
                            imageBase64 = s;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        products.Add(new ProductDto(odooId, name, type, listPrice, stdPrice, sku, barcode, catId, catName, variantCount, imageBase64, null, null, null));
                    }
                }
            }

            UpdateStatus(true, "Odoo Online");
            return products;
        }
        catch
        {
            if (allowOfflineSimulation) return GetSimulatedOdooProducts();
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    /// <summary>
    /// Fetches combo choices and combo lines from Odoo Online (pos.combo & pos.combo.line)
    /// </summary>
    public async Task<(List<ComboChoiceDto> combos, List<ComboChoiceLineDto> lines)> FetchOdooComboChoicesAsync(
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
                if (allowOfflineSimulation) return GetSimulatedOdooCombos();
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var comboPayload = new
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
                        "pos.combo",
                        "search_read",
                        new object[] { new object[] { } },
                        new { fields = new[] { "id", "name", "combo_line_ids" } }
                    }
                },
                id = 55
            };

            var comboReq = new StringContent(JsonSerializer.Serialize(comboPayload), Encoding.UTF8, "application/json");
            var comboResp = await _httpClient.PostAsync(endpoint, comboReq);
            comboResp.EnsureSuccessStatusCode();

            string comboStr = await comboResp.Content.ReadAsStringAsync();
            using var comboDoc = JsonDocument.Parse(comboStr);

            var combos = new List<ComboChoiceDto>();
            if (comboDoc.RootElement.TryGetProperty("result", out var cList) && cList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    var lineIds = new List<int>();
                    if (item.TryGetProperty("combo_line_ids", out var lids) && lids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var id in lids.EnumerateArray()) lineIds.Add(id.GetInt32());
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        combos.Add(new ComboChoiceDto(odooId, name, lineIds));
                    }
                }
            }

            // Fetch combo lines
            var linesPayload = new
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
                        "pos.combo.line",
                        "search_read",
                        new object[] { new object[] { } },
                        new { fields = new[] { "id", "combo_id", "product_id", "combo_price" } }
                    }
                },
                id = 56
            };

            var lineReq = new StringContent(JsonSerializer.Serialize(linesPayload), Encoding.UTF8, "application/json");
            var lineResp = await _httpClient.PostAsync(endpoint, lineReq);
            lineResp.EnsureSuccessStatusCode();

            string lineStr = await lineResp.Content.ReadAsStringAsync();
            using var lineDoc = JsonDocument.Parse(lineStr);

            var lines = new List<ComboChoiceLineDto>();
            if (lineDoc.RootElement.TryGetProperty("result", out var lList) && lList.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in lList.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    int comboId = 0;
                    if (item.TryGetProperty("combo_id", out var cid) && cid.ValueKind == JsonValueKind.Array && cid.GetArrayLength() > 0)
                    {
                        comboId = cid[0].GetInt32();
                    }
                    int? prodId = null;
                    string prodName = "";
                    if (item.TryGetProperty("product_id", out var pid) && pid.ValueKind == JsonValueKind.Array && pid.GetArrayLength() >= 2)
                    {
                        prodId = pid[0].GetInt32();
                        prodName = pid[1].GetString() ?? "";
                    }
                    decimal extraPrice = item.TryGetProperty("combo_price", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetDecimal() : 0.00m;

                    lines.Add(new ComboChoiceLineDto(odooId, comboId, prodId, prodName, 10.00m, extraPrice));
                }
            }

            UpdateStatus(true, "Odoo Online");
            return (combos, lines);
        }
        catch
        {
            if (allowOfflineSimulation) return GetSimulatedOdooCombos();
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    public static List<ProductDto> GetSimulatedOdooProducts()
    {
        return
        [
            new(1, "Acoustic Bloc Screens", "consu", 295.00m, 180.00m, "DEMO-0008", "08472910384", 2, "Desks", 2, null, "[\"Bestseller\", \"Featured\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"White, Black\"}]", null),
            new(2, "Black embroidered t-shirt", "consu", 35.00m, 12.50m, "E-COM11", "09182371928", 4, "Upper body", 8, null, "[\"New Arrival\"]", "[{\"AttributeName\":\"Size\",\"Values\":\"S, M, L, XL\"},{\"AttributeName\":\"Color\",\"Values\":\"Black, White\"}]", null),
            new(3, "Office Chair Black", "consu", 159.00m, 85.00m, "FURN-0012", "07182937461", 3, "Chairs", 1, null, "[\"Featured\"]", null, null),
            new(4, "Large Meeting Table", "consu", 850.00m, 450.00m, "CONF-0001", "08291048291", 2, "Desks", 3, null, "[\"Bestseller\"]", "[{\"AttributeName\":\"Size\",\"Values\":\"Small, Medium, Large\"}]", null),
            new(5, "Desk Pad", "consu", 19.99m, 8.00m, "MISC-0034", "01928471928", 1, "Misc", 1, null, "[\"Special Offer\"]", null, null),
            new(6, "Lunch Burger Combo", "combo", 14.50m, 7.00m, "COMBO-001", "09988112233", 6, "Others", 1, null, "[\"Featured\"]", null, "[{\"ComboName\":\"Burger Choice\",\"ItemsCount\":\"4 items\"},{\"ComboName\":\"Drink Choice\",\"ItemsCount\":\"4 items\"},{\"ComboName\":\"Side Choice\",\"ItemsCount\":\"3 items\"}]"),
            new(7, "Breakfast Pastry Combo", "combo", 8.50m, 3.50m, "COMBO-002", "09988112244", 7, "Breads", 1, null, "[\"Bestseller\"]", null, "[{\"ComboName\":\"Pastry Choice\",\"ItemsCount\":\"3 items\"},{\"ComboName\":\"Drink Choice\",\"ItemsCount\":\"4 items\"}]"),
            new(8, "Croissant", "consu", 3.50m, 1.20m, "BAKE-0001", "06677889900", 8, "Pastries", 1, null, "[\"Organic\"]", null, null),
            new(9, "Baguette", "consu", 2.80m, 0.90m, "BAKE-0002", "06677889911", 7, "Breads", 1, null, "[\"Organic\"]", null, null),
            new(10, "Maintenance Service", "service", 75.00m, 30.00m, "SERV-0005", "05544332211", 1, "Misc", 1, null, null, null, null),
            new(11, "Ergonomic Desk", "consu", 420.00m, 260.00m, "FURN-0055", "03322114455", 2, "Desks", 2, null, "[\"Featured\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"White, Oak\"}]", null),
            new(12, "Conference Chair", "consu", 110.00m, 60.00m, "FURN-0023", "04455667788", 3, "Chairs", 4, null, "[\"Bestseller\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"Grey, Black, Blue, Red\"}]", null)
        ];
    }

    public static (List<ComboChoiceDto> combos, List<ComboChoiceLineDto> lines) GetSimulatedOdooCombos()
    {
        var combos = new List<ComboChoiceDto>
        {
            new(1, "Burger Choice", [1, 2, 3, 4]),
            new(2, "Drink Choice", [5, 6, 7, 8]),
            new(3, "Side Choice", [9, 10, 11]),
            new(4, "Pastry Choice", [12, 13, 14])
        };

        var lines = new List<ComboChoiceLineDto>
        {
            new(1, 1, 101, "Classic Beef Burger", 12.00m, 0.00m),
            new(2, 1, 102, "Double Cheeseburger", 14.00m, 2.00m),
            new(3, 1, 103, "Veggie Burger", 11.50m, 0.00m),
            new(4, 1, 104, "Crispy Chicken Burger", 13.00m, 1.00m),

            new(5, 2, 201, "Soda Can 330ml", 2.50m, 0.00m),
            new(6, 2, 202, "Mineral Water 500ml", 2.00m, 0.00m),
            new(7, 2, 203, "Fresh Orange Juice", 4.50m, 1.50m),
            new(8, 2, 204, "Iced Lemon Tea", 3.50m, 0.50m),

            new(9, 3, 301, "French Fries", 3.50m, 0.00m),
            new(10, 3, 302, "Onion Rings", 4.00m, 0.50m),
            new(11, 3, 303, "Caesar Salad", 5.00m, 1.50m),

            new(12, 4, 401, "Butter Croissant", 3.50m, 0.00m),
            new(13, 4, 402, "Pain au Chocolat", 4.00m, 0.50m),
            new(14, 4, 403, "Almond Danish", 4.50m, 1.00m)
        };

        return (combos, lines);
    }

    public async Task<List<PosSessionDto>> FetchOdooSessionsAsync(bool allowOfflineSimulation = true)
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                if (allowOfflineSimulation) return GetSimulatedOdooSessions();
                throw new InvalidOperationException("Odoo Server URL is not configured.");
            }

            string baseUrl = NormalizeUrl(config.ServerUrl);
            string endpoint = $"{baseUrl}/jsonrpc";

            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation) return GetSimulatedOdooSessions();
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var payload = new
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
                        "pos.session",
                        "search_read",
                        new object[] { new object[] { } },
                        new { fields = new[] { "id", "name", "config_id", "user_id", "start_at", "stop_at", "state", "cash_register_balance_start", "cash_register_balance_end_real", "order_count", "total_payments_amount" } }
                    }
                },
                id = 60
            };

            var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(endpoint, req);
            resp.EnsureSuccessStatusCode();

            string resStr = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resStr);

            var list = new List<PosSessionDto>();
            if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    int? cfgId = null;
                    string cfgName = "";
                    if (item.TryGetProperty("config_id", out var cid) && cid.ValueKind == JsonValueKind.Array && cid.GetArrayLength() >= 2)
                    {
                        cfgId = cid[0].GetInt32();
                        cfgName = cid[1].GetString() ?? "";
                    }
                    int? userId = null;
                    string userName = "";
                    if (item.TryGetProperty("user_id", out var uidProp) && uidProp.ValueKind == JsonValueKind.Array && uidProp.GetArrayLength() >= 2)
                    {
                        userId = uidProp[0].GetInt32();
                        userName = uidProp[1].GetString() ?? "";
                    }
                    DateTime? startAt = null;
                    if (item.TryGetProperty("start_at", out var sProp) && sProp.ValueKind == JsonValueKind.String && DateTime.TryParse(sProp.GetString(), out var st))
                    {
                        startAt = st;
                    }
                    DateTime? stopAt = null;
                    if (item.TryGetProperty("stop_at", out var spProp) && spProp.ValueKind == JsonValueKind.String && DateTime.TryParse(spProp.GetString(), out var sp))
                    {
                        stopAt = sp;
                    }
                    string state = item.TryGetProperty("state", out var stp) && stp.ValueKind == JsonValueKind.String ? stp.GetString() ?? "opened" : "opened";
                    decimal openBal = item.TryGetProperty("cash_register_balance_start", out var ob) && ob.ValueKind == JsonValueKind.Number ? ob.GetDecimal() : 0.00m;
                    decimal closeBal = item.TryGetProperty("cash_register_balance_end_real", out var cb) && cb.ValueKind == JsonValueKind.Number ? cb.GetDecimal() : 0.00m;
                    int ordersCount = item.TryGetProperty("order_count", out var oc) && oc.ValueKind == JsonValueKind.Number ? oc.GetInt32() : 0;
                    decimal totalPayments = item.TryGetProperty("total_payments_amount", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetDecimal() : 0.00m;

                    list.Add(new PosSessionDto(odooId, name, cfgId, cfgName, userId, userName, null, startAt, stopAt, state, openBal, closeBal, ordersCount, totalPayments, userName));
                }
            }

            UpdateStatus(true, "Odoo Online");
            return list.Count > 0 ? list : GetSimulatedOdooSessions();
        }
        catch
        {
            if (allowOfflineSimulation) return GetSimulatedOdooSessions();
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    public async Task<(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments)> FetchOdooOrdersAsync(bool allowOfflineSimulation = true)
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                if (allowOfflineSimulation) return GetSimulatedOdooOrders();
                throw new InvalidOperationException("Odoo Server URL is not configured.");
            }

            string baseUrl = NormalizeUrl(config.ServerUrl);
            string endpoint = $"{baseUrl}/jsonrpc";

            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation) return GetSimulatedOdooOrders();
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var payload = new
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
                        "pos.order",
                        "search_read",
                        new object[] { new object[] { } },
                        new { fields = new[] { "id", "name", "pos_reference", "tracking_number", "session_id", "date_order", "user_id", "fiscal_position_id", "amount_total", "amount_tax", "amount_paid", "margin", "margin_ratio", "state" } }
                    }
                },
                id = 61
            };

            var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(endpoint, req);
            resp.EnsureSuccessStatusCode();

            string resStr = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resStr);

            var orders = new List<PosOrderDto>();
            if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    string posRef = item.TryGetProperty("pos_reference", out var pr) && pr.ValueKind == JsonValueKind.String ? pr.GetString() ?? "" : "";
                    string trackNum = item.TryGetProperty("tracking_number", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() ?? "" : "";
                    int? sessOdooId = null;
                    string sessName = "";
                    if (item.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.Array && sid.GetArrayLength() >= 2)
                    {
                        sessOdooId = sid[0].GetInt32();
                        sessName = sid[1].GetString() ?? "";
                    }
                    string cfgName = sessName.Contains('/') ? sessName.Split('/')[0].Trim() : "Point of Sale";
                    DateTime? dateOrder = null;
                    if (item.TryGetProperty("date_order", out var doProp) && doProp.ValueKind == JsonValueKind.String && DateTime.TryParse(doProp.GetString(), out var dt))
                    {
                        dateOrder = dt;
                    }
                    int? cashierId = null;
                    string cashierName = "";
                    if (item.TryGetProperty("user_id", out var uidProp) && uidProp.ValueKind == JsonValueKind.Array && uidProp.GetArrayLength() >= 2)
                    {
                        cashierId = uidProp[0].GetInt32();
                        cashierName = uidProp[1].GetString() ?? "";
                    }
                    string fiscal = item.TryGetProperty("fiscal_position_id", out var fp) && fp.ValueKind == JsonValueKind.Array && fp.GetArrayLength() >= 2
                        ? fp[1].GetString() ?? "Standard Tax" : "Standard Tax";

                    decimal amountTotal = item.TryGetProperty("amount_total", out var at) && at.ValueKind == JsonValueKind.Number ? at.GetDecimal() : 0.00m;
                    decimal amountTax = item.TryGetProperty("amount_tax", out var atx) && atx.ValueKind == JsonValueKind.Number ? atx.GetDecimal() : 0.00m;
                    decimal amountPaid = item.TryGetProperty("amount_paid", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetDecimal() : amountTotal;
                    decimal margin = item.TryGetProperty("margin", out var mg) && mg.ValueKind == JsonValueKind.Number ? mg.GetDecimal() : amountTotal * 0.45m;
                    decimal marginRatio = item.TryGetProperty("margin_ratio", out var mr) && mr.ValueKind == JsonValueKind.Number ? mr.GetDecimal() : 0.45m;
                    string state = item.TryGetProperty("state", out var stp) && stp.ValueKind == JsonValueKind.String ? stp.GetString() ?? "paid" : "paid";

                    orders.Add(new PosOrderDto(odooId, name, posRef, trackNum, null, sessOdooId, sessName, cfgName, dateOrder, cashierId, cashierName, null, fiscal, amountTotal, amountTax, amountPaid, margin, marginRatio, state, false, "My Company", "", null, null));
                }
            }

            UpdateStatus(true, "Odoo Online");
            return orders.Count > 0 ? (orders, new List<PosOrderLineDto>(), new List<PosPaymentDto>()) : GetSimulatedOdooOrders();
        }
        catch
        {
            if (allowOfflineSimulation) return GetSimulatedOdooOrders();
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    public async Task<List<PosPaymentDto>> FetchOdooPaymentsAsync(bool allowOfflineSimulation = true)
    {
        try
        {
            var config = await SqliteDatabaseService.Instance.GetOdooConfigAsync();
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
            {
                if (allowOfflineSimulation) return GetSimulatedOdooPayments();
                throw new InvalidOperationException("Odoo Server URL is not configured.");
            }

            string baseUrl = NormalizeUrl(config.ServerUrl);
            string endpoint = $"{baseUrl}/jsonrpc";

            var test = await TestConnectionAsync(config);
            if (!test.Success || !test.Uid.HasValue)
            {
                if (allowOfflineSimulation) return GetSimulatedOdooPayments();
                throw new InvalidOperationException(test.Message);
            }

            int uid = test.Uid.Value;

            var payload = new
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
                        "pos.payment",
                        "search_read",
                        new object[] { new object[] { } },
                        new { fields = new[] { "id", "pos_order_id", "session_id", "payment_method_id", "amount", "payment_date" } }
                    }
                },
                id = 62
            };

            var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _httpClient.PostAsync(endpoint, req);
            resp.EnsureSuccessStatusCode();

            string resStr = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resStr);

            var list = new List<PosPaymentDto>();
            if (doc.RootElement.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    int odooId = item.GetProperty("id").GetInt32();
                    int? orderOdooId = null;
                    string orderName = "";
                    if (item.TryGetProperty("pos_order_id", out var oid) && oid.ValueKind == JsonValueKind.Array && oid.GetArrayLength() >= 2)
                    {
                        orderOdooId = oid[0].GetInt32();
                        orderName = oid[1].GetString() ?? "";
                    }
                    int? sessionOdooId = null;
                    string sessionName = "";
                    if (item.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.Array && sid.GetArrayLength() >= 2)
                    {
                        sessionOdooId = sid[0].GetInt32();
                        sessionName = sid[1].GetString() ?? "";
                    }
                    int? methodId = null;
                    string methodName = "Cash";
                    if (item.TryGetProperty("payment_method_id", out var pmid) && pmid.ValueKind == JsonValueKind.Array && pmid.GetArrayLength() >= 2)
                    {
                        methodId = pmid[0].GetInt32();
                        methodName = pmid[1].GetString() ?? "Cash";
                    }
                    decimal amount = item.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number ? am.GetDecimal() : 0.00m;
                    DateTime? payDate = null;
                    if (item.TryGetProperty("payment_date", out var pd) && pd.ValueKind == JsonValueKind.String && DateTime.TryParse(pd.GetString(), out var pdt))
                    {
                        payDate = pdt;
                    }

                    list.Add(new PosPaymentDto(odooId, orderOdooId, orderName, sessionOdooId, sessionName, "Cashier", methodId, methodName, amount, "USD ($)", "—", payDate));
                }
            }

            UpdateStatus(true, "Odoo Online");
            return list.Count > 0 ? list : GetSimulatedOdooPayments();
        }
        catch
        {
            if (allowOfflineSimulation) return GetSimulatedOdooPayments();
            UpdateStatus(false, "Offline (Local Mode)");
            throw;
        }
    }

    public static List<PosSessionRecord> GetSimulatedSessionsRecords()
    {
        return GetSimulatedOdooSessions().Select(s => new PosSessionRecord
        {
            OdooId = s.OdooId,
            Name = s.Name,
            PosConfigId = s.PosConfigId,
            PosConfigName = s.PosConfigName ?? "",
            UserId = s.UserId,
            UserName = s.UserName ?? "",
            StartAt = s.StartAt,
            StopAt = s.StopAt,
            State = s.State,
            OpeningBalance = s.OpeningBalance,
            ClosingBalance = s.ClosingBalance,
            OrdersCount = s.OrdersCount,
            TotalPaymentsAmount = s.TotalPaymentsAmount,
            Employees = s.Employees
        }).ToList();
    }

    public static List<PosSessionDto> GetSimulatedOdooSessions()
    {
        return
        [
            new(4, "Bakery Shop/00004", 3, "Bakery Shop", 3, "Mitchell Admin", null, new DateTime(2026, 10, 31, 8, 30, 0), null, "opened", 100.00m, 0.00m, 4, 248.50m, "Mitchell Admin, Marc Demo"),
            new(3, "Bakery Shop/00003", 3, "Bakery Shop", 1, "Administrator", null, new DateTime(2026, 10, 30, 8, 0, 0), new DateTime(2026, 10, 30, 18, 0, 0), "closed", 100.00m, 684.20m, 12, 584.20m, "Administrator"),
            new(2, "Furniture Shop/00002", 1, "Furniture Shop", 2, "Marc Demo", null, new DateTime(2026, 10, 31, 9, 15, 0), null, "opened", 150.00m, 0.00m, 3, 1295.00m, "Marc Demo"),
            new(1, "Clothes Shop/00001", 2, "Clothes Shop", 3, "Mitchell Admin", null, new DateTime(2026, 10, 29, 10, 0, 0), new DateTime(2026, 10, 29, 20, 0, 0), "closed", 80.00m, 425.00m, 8, 345.00m, "Mitchell Admin")
        ];
    }

    public static (List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto> payments) GetSimulatedOdooOrders()
    {
        var orders = new List<PosOrderDto>
        {
            new(101, "Bakery Shop - 000001", "Order 00001-002-0001", "00001", null, 4, "Bakery Shop/00004", "Bakery Shop", new DateTime(2026, 10, 31, 9, 12, 15), 3, "Mitchell Admin", null, "Standard Tax", 24.50m, 3.20m, 24.50m, 11.20m, 0.457m, "paid", false, "My Company", "", null, null),
            new(102, "Bakery Shop - 000002", "Order 00001-002-0002", "00002", null, 4, "Bakery Shop/00004", "Bakery Shop", new DateTime(2026, 10, 31, 10, 45, 0), 3, "Mitchell Admin", null, "Standard Tax", 42.00m, 5.48m, 42.00m, 20.10m, 0.478m, "done", false, "My Company", "", null, null),
            new(103, "Furniture Shop - 000001", "Order 00002-001-0001", "00001", null, 2, "Furniture Shop/00002", "Furniture Shop", new DateTime(2026, 10, 31, 11, 20, 0), 2, "Marc Demo", null, "Standard Tax", 454.00m, 59.22m, 454.00m, 189.00m, 0.416m, "paid", false, "My Company", "", null, null),
            new(104, "Bakery Shop - 000003", "Order 00001-002-0003", "00003", null, 4, "Bakery Shop/00004", "Bakery Shop", new DateTime(2026, 10, 31, 13, 5, 0), 3, "Mitchell Admin", null, "Standard Tax", 18.00m, 2.35m, 18.00m, 8.50m, 0.472m, "cancel", false, "My Company", "", null, null)
        };

        var lines = new List<PosOrderLineDto>
        {
            new(1001, 101, 7, "Breakfast Pastry Combo", false, 2m, 8.50m, 0m, "15%", 14.78m, 17.00m),
            new(1002, 101, 8, "Croissant", false, 2m, 3.50m, 0m, "15%", 6.09m, 7.00m),
            new(1003, 101, 6, "Mineral Water 500ml", false, 1m, 2.00m, 75m, "15%", 0.43m, 0.50m),
            new(1004, 102, 6, "Lunch Burger Combo", false, 2m, 14.50m, 0m, "15%", 25.22m, 29.00m),
            new(1005, 102, 1, "Acoustic Bloc Screens", false, 1m, 13.00m, 0m, "15%", 11.30m, 13.00m),
            new(1006, 103, 11, "Ergonomic Desk", false, 1m, 420.00m, 0m, "15%", 365.22m, 420.00m),
            new(1007, 103, 8, "Soda Can 330ml", false, 2m, 2.50m, 0m, "15%", 4.35m, 5.00m),
            new(1008, 103, 5, "Desk Pad", false, 1m, 29.00m, 0m, "15%", 25.21m, 29.00m),
            new(1009, 104, 8, "Croissant", false, 3m, 3.50m, 0m, "15%", 9.13m, 10.50m),
            new(1010, 104, 7, "Breakfast Pastry Combo", false, 1m, 7.50m, 0m, "15%", 6.52m, 7.50m)
        };

        var payments = GetSimulatedOdooPayments();
        return (orders, lines, payments);
    }

    public static List<PosPaymentDto> GetSimulatedOdooPayments()
    {
        return
        [
            new(201, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", 1, "Cash", 24.50m, "USD ($)", "—", new DateTime(2026, 10, 31, 9, 12, 15)),
            new(202, 102, "Bakery Shop - 000002", 4, "Bakery Shop/00004", "Mitchell Admin", 2, "Bank", 42.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 10, 45, 0)),
            new(203, 103, "Furniture Shop - 000001", 2, "Furniture Shop/00002", "Marc Demo", 2, "Bank", 454.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 11, 20, 0)),
            new(204, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", 1, "Cash", 35.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 14, 10, 0)),
            new(205, 102, "Bakery Shop - 000002", 4, "Bakery Shop/00004", "Mitchell Admin", 1, "Cash", 55.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 14, 35, 0)),
            new(206, 103, "Furniture Shop - 000001", 2, "Furniture Shop/00002", "Marc Demo", 2, "Bank", 841.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 15, 50, 0)),
            new(207, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", 1, "Cash", 92.00m, "USD ($)", "—", new DateTime(2026, 10, 31, 16, 15, 0))
        ];
    }
}
