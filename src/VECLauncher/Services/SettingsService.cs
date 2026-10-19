using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VECLauncher.Services;

public sealed partial class LauncherSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("lastVersion")]
    private string? _lastVersion;

    [ObservableProperty]
    [property: JsonPropertyName("lastInstanceId")]
    private string? _lastInstanceId;

    [ObservableProperty]
    [property: JsonPropertyName("minMemoryMb")]
    private int _minMemoryMb = 1024;

    [ObservableProperty]
    [property: JsonPropertyName("maxMemoryMb")]
    private int _maxMemoryMb = 4096;

    [ObservableProperty]
    [property: JsonPropertyName("windowWidth")]
    private int _windowWidth = 1280;

    [ObservableProperty]
    [property: JsonPropertyName("windowHeight")]
    private int _windowHeight = 720;

    [ObservableProperty]
    [property: JsonPropertyName("fullscreen")]
    private bool _fullscreen;

    [ObservableProperty]
    [property: JsonPropertyName("showSnapshots")]
    private bool _showSnapshots;

    [ObservableProperty]
    [property: JsonPropertyName("closeOnLaunch")]
    private bool _closeLauncherOnStart;

    [ObservableProperty]
    [property: JsonPropertyName("showConsole")]
    private bool _showConsole;

    [ObservableProperty]
    [property: JsonPropertyName("serverAddress")]
    private string _serverAddress = LauncherConfig.DefaultMinecraftServer;

    [ObservableProperty]
    [property: JsonPropertyName("webServerUrl")]
    private string _webServerUrl = LauncherConfig.DefaultWebServerUrl;

    [ObservableProperty]
    [property: JsonPropertyName("extraJvmArgs")]
    private string _extraJvmArgs = "";

    [ObservableProperty]
    [property: JsonPropertyName("gameDir")]
    private string _gameDir = "";

    [ObservableProperty]
    [property: JsonPropertyName("customJavaPath")]
    private string _customJavaPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("allowMultipleInstances")]
    private bool _allowMultipleInstances;

    [ObservableProperty]
    [property: JsonPropertyName("minimizeOnLaunch")]
    private bool _minimizeOnLaunch = true;

    [ObservableProperty]
    [property: JsonPropertyName("confirmGameStop")]
    private bool _confirmGameStop = true;

    [ObservableProperty]
    [property: JsonPropertyName("accentColor")]
    private string _accentColor = "#FACC15";

    [ObservableProperty]
    [property: JsonPropertyName("backgroundStyle")]
    private string _backgroundStyle = "Тёмный";

    [ObservableProperty]
    [property: JsonPropertyName("customBannerPath")]
    private string _customBannerPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("cornerRadius")]
    private int _cornerRadius = 12;

    [ObservableProperty]
    [property: JsonPropertyName("animations")]
    private bool _animations = true;

    [ObservableProperty]
    [property: JsonPropertyName("compactMode")]
    private bool _compactMode;

    [ObservableProperty]
    [property: JsonPropertyName("defaultIsolated")]
    private bool _defaultIsolated;

    [ObservableProperty]
    [property: JsonPropertyName("theme")]
    private string _theme = "Тёмная";

    [ObservableProperty]
    [property: JsonPropertyName("windowBackground")]
    private string _windowBackgroundPath = "";

    [ObservableProperty]
    [property: JsonPropertyName("windowBackgroundOpacity")]
    private double _windowBackgroundOpacity = 0.35;

    [ObservableProperty]
    [property: JsonPropertyName("gameLanguage")]
    private string _gameLanguage = "ru";

    [ObservableProperty]
    [property: JsonPropertyName("autoLanguage")]
    private bool _autoSetGameLanguage = true;

    [ObservableProperty]
    [property: JsonPropertyName("customTheme")]
    private string _customThemeJson = "";

    partial void OnCloseLauncherOnStartChanged(bool value)
    {
        if (value && MinimizeOnLaunch) MinimizeOnLaunch = false;
    }

    partial void OnMinimizeOnLaunchChanged(bool value)
    {
        if (value && CloseLauncherOnStart) CloseLauncherOnStart = false;
    }

    public static int RecommendedMaxMemory()
    {
        try
        {
            var totalMb = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
            if (totalMb <= 0) return 4096;
            var half = (int)(totalMb / 2);
            return Math.Clamp(half - half % 512, 2048, 8192);
        }
        catch { return 4096; }
    }

    public static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Золото VEC", "#FACC15"),
        ("Океан", "#38BDF8"),
        ("Аметист", "#A78BFA"),
        ("Закат", "#FB923C"),
        ("Роза", "#FB7185"),
        ("Бирюза", "#2DD4BF"),
        ("Индиго", "#818CF8")
    };
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(LauncherPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(LauncherPaths.SettingsFile));
                if (s is not null)
                {
                    if (string.IsNullOrWhiteSpace(s.GameDir)) s.GameDir = LauncherPaths.Root;
                    if (string.IsNullOrWhiteSpace(s.AccentColor) ||
                        s.AccentColor.Equals("#22C55E", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#10B981", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#4ADE80", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#14301F", StringComparison.OrdinalIgnoreCase))
                    {
                        s.AccentColor = "#FACC15";
                    }
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SettingsService: failed to load settings: {ex.Message}");
        }

        return new LauncherSettings
        {
            MaxMemoryMb = LauncherSettings.RecommendedMaxMemory(),
            GameDir = LauncherPaths.Root
        };
    }

    public static void Save(LauncherSettings settings)
    {
        try
        {
            LauncherPaths.EnsureAll();
            File.WriteAllText(LauncherPaths.SettingsFile, JsonSerializer.Serialize(settings, Opts));
        }
        catch (Exception ex)
        {
            Log.Warn($"SettingsService: failed to save settings: {ex.Message}");
        }
    }
}