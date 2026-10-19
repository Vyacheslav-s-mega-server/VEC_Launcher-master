using System.Text.Json.Serialization;

namespace VECLauncher.Installer.Models;

public sealed class ReleaseManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }
}
