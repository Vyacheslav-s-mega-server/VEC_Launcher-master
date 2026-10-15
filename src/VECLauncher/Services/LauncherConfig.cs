using System;

namespace VECLauncher.Services;

public static class LauncherConfig
{
    public const string ProductionWebServerUrl = "https://vec.kpvk.edu.kz";
    public const string DevelopmentWebServerUrl = "http://localhost:5044";
    public const string DefaultMinecraftServer = "95.59.233.227:25565";
    public const string ProjectWebsiteUrl = "https://vec.kpvk.edu.kz/";

    public static string DefaultWebServerUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("VEC_SERVER_URL");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env.TrimEnd('/');
            }

#if DEBUG
            return DevelopmentWebServerUrl;
#else
            return ProductionWebServerUrl;
#endif
        }
    }
}
