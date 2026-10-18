using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VECLauncher.Services;

public static class VecAuthService
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
}