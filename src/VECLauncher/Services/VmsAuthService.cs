using System;
using System.Net.Http;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public static class VmsAuthService
{
    public static string DefaultVmsServerUrl
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("VMS_SERVER_URL") ?? Environment.GetEnvironmentVariable("VEC_SERVER_URL");
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
#if DEBUG
            return LauncherConfig.DefaultWebServerUrl;
#else
            try
            {
                Settings s = SettingsService.Load();
                if (!string.IsNullOrWhiteSpace(s.WebServerUrl)) return s.WebServerUrl.TrimEnd('/');
            }
            catch { }
            return LauncherConfig.DefaultWebServerUrl;
#endif
        }
    }

    public static string DefaultVecServerUrl => DefaultVmsServerUrl;

    public static async Task<string> GetActiveServerUrlAsync()
    {
        string targetUrl = DefaultVmsServerUrl;
        try
        {
            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            HttpResponseMessage resp = await http.GetAsync($"{targetUrl}/api/status");
            if (resp.IsSuccessStatusCode) return targetUrl;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return targetUrl;
    }
}
