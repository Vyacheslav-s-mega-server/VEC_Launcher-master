using System.Text.Json.Serialization;

namespace VECLauncher.Models;

public enum LoaderKind
{
    Vanilla = 0,
    Fabric = 1,
    Forge = 2,
    NeoForge = 3
}

public static class LoaderKindExtensions
{
    public static string Display(this LoaderKind k) => k switch
    {
        LoaderKind.Vanilla => "Vanilla",
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Forge => "Forge",
        LoaderKind.NeoForge => "NeoForge",
        _ => k.ToString()
    };
}

public sealed class GameInstance
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    [JsonPropertyName("name")] public string Name { get; set; } = "Новая сборка";
    [JsonPropertyName("mcVersion")] public string McVersion { get; set; } = "";
    [JsonPropertyName("loader")] public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    [JsonPropertyName("loaderVersion")] public string? LoaderVersion { get; set; }
    [JsonPropertyName("launchVersionId")] public string? LaunchVersionId { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("lastPlayed")] public DateTimeOffset? LastPlayed { get; set; }
    [JsonPropertyName("totalPlaySeconds")] public long TotalPlaySeconds { get; set; }
    [JsonPropertyName("maxMemoryMb")] public int MaxMemoryMb { get; set; }
    [JsonPropertyName("extraJvmArgs")] public string ExtraJvmArgs { get; set; } = "";
    [JsonPropertyName("isolated")] public bool Isolated { get; set; }
    [JsonPropertyName("javaPath")] public string JavaPath { get; set; } = "";
    [JsonPropertyName("windowWidth")] public int WindowWidth { get; set; }
    [JsonPropertyName("windowHeight")] public int WindowHeight { get; set; }
    [JsonPropertyName("serverAddress")] public string ServerAddress { get; set; } = "";
    [JsonPropertyName("jvmPreset")] public string JvmPreset { get; set; } = "Стандарт";

    public void AddSession(long seconds)
    {
        if (seconds < 5) return;
        TotalPlaySeconds += seconds;
        LastPlayed = DateTimeOffset.Now;
    }

    [JsonIgnore] public string EffectiveVersionId => LaunchVersionId ?? McVersion;

    [JsonIgnore]
    public string LoaderDisplay => Loader == LoaderKind.Vanilla
        ? "Vanilla"
        : $"{Loader.Display()} {LoaderVersion}";

    [JsonIgnore]
    public string PlayTimeDisplay
    {
        get
        {
            if (TotalPlaySeconds < 60) return "менее минуты";
            var ts = TimeSpan.FromSeconds(TotalPlaySeconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} ч {ts.Minutes} мин";
            return $"{ts.Minutes} мин";
        }
    }

    public override string ToString() => Name;
}

public sealed class ServerStatus
{
    public bool Online { get; init; }
    public int OnlinePlayers { get; init; }
    public int MaxPlayers { get; init; }
    public string VersionName { get; init; } = "";
    public int ProtocolVersion { get; init; }
    public string Motd { get; init; } = "";
    public long PingMs { get; init; }
    public byte[]? FaviconPng { get; init; }
    public string? Error { get; init; }

    public static ServerStatus Offline(string error) => new() { Online = false, Error = error };

    public string PlayersDisplay => Online ? $"{OnlinePlayers} / {MaxPlayers}" : "—";
}