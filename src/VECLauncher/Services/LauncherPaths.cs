namespace VECLauncher.Services;

public static class LauncherPaths
{
    private const string FolderName = ".veclauncher";

    public const string PortableMarker = "portable.txt";

    public static bool IsPortable { get; private set; }

    public static string Root { get; private set; } = ResolveRoot();

    public static string ExeDir
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
            }
            catch (Exception ex) { Log.Warn(ex.Message); }

            return AppContext.BaseDirectory;
        }
    }

    private static string PortableRoot => Path.Combine(ExeDir, "VECLauncherData");

    private static string ResolveRoot()
    {
        try
        {
            if (File.Exists(Path.Combine(ExeDir, PortableMarker)))
            {
                IsPortable = true;
                return PortableRoot;
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        IsPortable = false;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, FolderName);
    }

    public static string VersionsDir => Path.Combine(Root, "versions");
    public static string LibrariesDir => Path.Combine(Root, "libraries");
    public static string AssetsDir => Path.Combine(Root, "assets");
    public static string AssetsIndexesDir => Path.Combine(AssetsDir, "indexes");
    public static string AssetsObjectsDir => Path.Combine(AssetsDir, "objects");
    public static string NativesRoot => Path.Combine(Root, "natives");
    public static string RuntimeDir => Path.Combine(Root, "runtime");
    public static string LogConfigsDir => Path.Combine(Root, "log_configs");
    public static string CacheDir => Path.Combine(Root, "cache");

    public static string AccountFile => Path.Combine(Root, "account.dat");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LauncherLogFile => Path.Combine(Root, "launcher.log");

    public static void EnsureAll()
    {
        foreach (var d in new[]
                 {
                     Root, VersionsDir, LibrariesDir, AssetsDir, AssetsIndexesDir,
                     AssetsObjectsDir, NativesRoot, RuntimeDir, LogConfigsDir, CacheDir
                 })
        {
            Directory.CreateDirectory(d);
        }
    }
}