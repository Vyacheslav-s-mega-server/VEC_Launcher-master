using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class VecAuthService
{
    public static string DefaultVecServerUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("VEC_SERVER_URL");
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
#if DEBUG
            return LauncherConfig.DefaultWebServerUrl;
#else
            try
            {
                var s = SettingsService.Load();
                if (!string.IsNullOrWhiteSpace(s.WebServerUrl)) return s.WebServerUrl.TrimEnd('/');
            }
            catch { }
            return LauncherConfig.DefaultWebServerUrl;
#endif
        }
    }
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<string> GetActiveServerUrlAsync()
    {
        var targetUrl = DefaultVecServerUrl;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"{targetUrl}/api/status");
            if (resp.IsSuccessStatusCode) return targetUrl;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return targetUrl;
    }

    public async Task<MinecraftAccount> LoginAsync(string login, string password, string? serverUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(serverUrl) ? DefaultVecServerUrl : serverUrl.TrimEnd('/');

        try
        {
            var payload = new
            {
                username = login,
                password = password,
                clientToken = Guid.NewGuid().ToString("N"),
                requestUser = true
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{url}/api/auth/login", content);

            if (!resp.IsSuccessStatusCode)
            {
                resp = await _http.PostAsync($"{url}/authserver/authenticate", content);
            }

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                var token = json?["accessToken"]?.ToString() ?? json?["sessionKey"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var userNode = json?["selectedProfile"] ?? json?["user"];
                var username = userNode?["name"]?.ToString() ?? userNode?["username"]?.ToString() ?? login;
                var uuid = userNode?["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var skinUrl = json?["skinUrl"]?.ToString() ?? userNode?["skinUrl"]?.ToString() ?? $"{url}/api/skin/{username}.png";
                var capeUrl = json?["capeUrl"]?.ToString() ?? userNode?["capeUrl"]?.ToString() ?? $"{url}/api/cape/{username}.png";
                var skinModel = json?["model"]?.ToString() ?? userNode?["model"]?.ToString() ?? "classic";

                return new MinecraftAccount
                {
                    Username = username,
                    Uuid = uuid,
                    AccessToken = token,
                    Type = AccountType.Vec,
                    ServerUrl = url,
                    SkinUrl = skinUrl,
                    CapeUrl = capeUrl,
                    SkinModel = skinModel,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
                };
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new Exception("Invalid login or password on VEC server.");
            }

            throw new Exception($"VEC server returned {resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"VecAuthService: VEC server error ({url}): {ex.Message}");
            throw;
        }
    }

    public async Task<MinecraftAccount> RegisterAsync(string username, string password, string? email = null, string? serverUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(serverUrl) ? DefaultVecServerUrl : serverUrl.TrimEnd('/');

        var payload = new
        {
            username = username,
            password = password,
            email = email ?? $"{username.ToLowerInvariant()}@vec.kpvk.edu.kz"
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{url}/api/auth/register", content);

        if (resp.IsSuccessStatusCode)
        {
            return await LoginAsync(username, password, url);
        }

        throw new Exception($"VEC server registration failed with status {resp.StatusCode}.");
    }
}