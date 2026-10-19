using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Installer.Models;

namespace VECLauncher.Installer.Services;

public interface IUpdateCheckService
{
    Task<ReleaseManifest> GetLatestReleaseAsync(CancellationToken ct = default);
}

public sealed class UpdateCheckService : IUpdateCheckService
{
    private readonly HttpClient _http;
    private const string DefaultServerUrl = "http://localhost:5044";
    private const string ProductionServerUrl = "https://vec.kpvk.edu.kz";
    private const string GitHubApiUrl = "https://api.github.com/repos/Vyacheslav-s-mega-server/VEC_Launcher-master/releases/latest";

    public UpdateCheckService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ReleaseManifest> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        // 0. Check local files adjacent to installer (instant offline/local testing)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidateZips = new[]
        {
            Path.Combine(baseDir, "VECLauncher-win-x64.zip"),
            Path.Combine(baseDir, "dist", "VECLauncher-win-x64.zip"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "VECLauncher-win-x64.zip")
        };

        foreach (var zip in candidateZips)
        {
            if (File.Exists(zip))
            {
                var fi = new FileInfo(zip);
                return new ReleaseManifest
                {
                    Version = "1.0.0-local",
                    DownloadUrl = Path.GetFullPath(zip),
                    FileSize = fi.Length,
                    Changelog = "Локальный тестовый пакет"
                };
            }
        }

        var candidateExes = new[]
        {
            Path.Combine(baseDir, "VECLauncher.exe"),
            Path.Combine(baseDir, "dist", "VECLauncher.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "dist", "VECLauncher.exe")
        };

        foreach (var exe in candidateExes)
        {
            if (File.Exists(exe))
            {
                var fi = new FileInfo(exe);
                return new ReleaseManifest
                {
                    Version = "1.0.0-local",
                    DownloadUrl = Path.GetFullPath(exe),
                    FileSize = fi.Length,
                    Changelog = "Локальный файл VECLauncher.exe"
                };
            }
        }

        // 1. Try local dev / production VEC server
        var serverCandidates = new[]
        {
            Environment.GetEnvironmentVariable("VEC_SERVER_URL")?.TrimEnd('/'),
            DefaultServerUrl,
            ProductionServerUrl
        };

        foreach (var server in serverCandidates)
        {
            if (string.IsNullOrEmpty(server)) continue;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var manifestUrl = $"{server}/api/launcher/version";
                using var resp = await _http.GetAsync(manifestUrl, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json);
                    if (manifest != null && !string.IsNullOrEmpty(manifest.DownloadUrl))
                    {
                        if (manifest.DownloadUrl.StartsWith("/"))
                        {
                            manifest.DownloadUrl = $"{server}{manifest.DownloadUrl}";
                        }
                        return manifest;
                    }
                }
            }
            catch
            {
                // Try next candidate
            }
        }

        // 2. Try GitHub Releases API
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "VEC_Launcher_Installer/1.0");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "1.0.0" : "1.0.0";
                var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("VECLauncher", StringComparison.OrdinalIgnoreCase))
                        {
                            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                            return new ReleaseManifest
                            {
                                Version = tagName.TrimStart('v', 'V'),
                                DownloadUrl = downloadUrl,
                                FileSize = size,
                                Changelog = body
                            };
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        // 3. Fallback direct download URL
        return new ReleaseManifest
        {
            Version = "1.0.0",
            DownloadUrl = $"{DefaultServerUrl}/download/VECLauncher-win-x64.zip",
            FileSize = 0
        };
    }
}
