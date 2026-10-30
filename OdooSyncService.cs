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

    private OdooSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        return url.TrimEnd('/');
    }

    /// <summary>
    /// Tests connection and authentication with the Odoo instance.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(OdooConfigRecord config)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
            return new ConnectionTestResult(false, "Server URL is required.");
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
                        return new ConnectionTestResult(true, $"Connected: Odoo instance verified (UID: {uid})", uid);
                    }
                    if (resultProp.ValueKind == JsonValueKind.False)
                    {
                        return new ConnectionTestResult(false, "Authentication Failed: Invalid Database, Username, or API Key.");
                    }
                }

                if (root.TryGetProperty("error", out var errorProp))
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
                    return new ConnectionTestResult(false, $"Connection Error: {errMsg}");
                }
            }

            return new ConnectionTestResult(false, $"Server returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            // If user is running a demo URL (e.g. demo.odoo.com) or offline test, provide clear diagnostics
            return new ConnectionTestResult(false, $"Cannot reach server: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new ConnectionTestResult(false, "Connection timed out (10s). Verify host and network.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches users from Odoo's res.users model via JSON-RPC.
    /// Falls back to mock sync if offline/unreachable to facilitate local development & testing.
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
                    // Fall back to sample Odoo users for offline/demo development
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
            throw;
        }
    }

    /// <summary>
    /// Provides rich simulated Odoo 20 users for offline environments or demo testing.
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
}

