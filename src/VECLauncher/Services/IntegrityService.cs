using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class IntegrityResult
{
    public bool Success { get; set; }
    public string? Ticket { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class BuildManifestFile
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("isRequired")] public bool IsRequired { get; set; }
    [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = "Core";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
}

public sealed class BuildManifestResponse
{
    [JsonPropertyName("build")] public string Build { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("files")] public List<BuildManifestFile> Files { get; set; } = new();
}

public static class IntegrityService
{
    private static readonly string[] ForbiddenPackKeywords = { "xray", "x-ray", "x_ray", "ore_finder", "orefinder", "see_through" };

    public static async Task<IntegrityResult> ValidateAndSyncAsync(
        GameInstance inst,
        MinecraftAccount account,
        string serverUrl,
        Action<string>? log,
        Action<string>? setStatus,
        CancellationToken ct)
    {
        var cleanServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
        var slug = "vec-fabric-1-21-11";

        setStatus?.Invoke("Проверка целостности сборки VEC...");
        log?.Invoke($"Запрос манифеста защиты с {cleanServerUrl}...");

        BuildManifestResponse? manifest = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var manifestJson = await http.GetStringAsync($"{cleanServerUrl}/api/builds/{slug}/manifest", ct);
            manifest = JsonSerializer.Deserialize<BuildManifestResponse>(manifestJson);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Предупреждение: не удалось получить манифест сборки: {ex.Message}");
        }

        var instDir = InstanceService.InstanceDir(inst);
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);

        if (manifest != null && manifest.Files.Count > 0)
        {
            var allowedMods = new HashSet<string>(manifest.Files.Where(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)).Select(f => f.FileName), StringComparer.OrdinalIgnoreCase);
            var localMods = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly);

            foreach (var localMod in localMods)
            {
                var modName = Path.GetFileName(localMod);
                if (!allowedMods.Contains(modName))
                {
                    try
                    {
                        File.Delete(localMod);
                        log?.Invoke($"[Античит] Удален посторонний мод: {modName}");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"[Античит] Не удалось удалить посторонний файл {modName}: {ex.Message}");
                    }
                }
            }

            using var sha = SHA256.Create();
            foreach (var manifestMod in manifest.Files)
            {
                var relPath = string.IsNullOrWhiteSpace(manifestMod.Path) ? ("mods/" + manifestMod.FileName) : manifestMod.Path;
                var targetFile = Path.Combine(instDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                var targetFolder = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                bool needsDownload = false;

                if (!File.Exists(targetFile))
                {
                    if (manifestMod.IsRequired)
                    {
                        needsDownload = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(manifestMod.Sha256) && manifestMod.Size > 0)
                {
                    try
                    {
                        using var stream = File.OpenRead(targetFile);
                        var computedHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        if (!string.Equals(computedHash, manifestMod.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            log?.Invoke($"[Античит] Обнаружена модификация {manifestMod.FileName}, восстанавливаю оригинал...");
                            File.Delete(targetFile);
                            needsDownload = true;
                        }
                    }
                    catch
                    {
                        needsDownload = true;
                    }
                }

                if (needsDownload && !string.IsNullOrWhiteSpace(manifestMod.DownloadUrl))
                {
                    setStatus?.Invoke($"Загрузка компонента сборки: {manifestMod.DisplayName}...");
                    log?.Invoke($"Скачивание файла: {manifestMod.FileName}...");
                    try
                    {
                        var url = manifestMod.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? manifestMod.DownloadUrl
                            : $"{cleanServerUrl}{manifestMod.DownloadUrl}";

                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("VEC_Launcher/3.0 (contact@vec.kpvk.edu.kz)");
                        var bytes = await http.GetByteArrayAsync(url, ct);
                        await File.WriteAllBytesAsync(targetFile, bytes, ct);
                        log?.Invoke($"Установлен компонент: {manifestMod.FileName}");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"Ошибка загрузки файла {manifestMod.FileName}: {ex.Message}");
                    }
                }
            }
        }

        var resourcepacksDir = Path.Combine(instDir, "resourcepacks");
        if (Directory.Exists(resourcepacksDir))
        {
            var packs = Directory.GetFiles(resourcepacksDir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var pack in packs)
            {
                var name = Path.GetFileName(pack).ToLowerInvariant();
                if (ForbiddenPackKeywords.Any(k => name.Contains(k)))
                {
                    try
                    {
                        File.Delete(pack);
                        log?.Invoke($"[Античит] Удален запрещенный ресурспак: {Path.GetFileName(pack)}");
                    }
                    catch { }
                }
            }
        }

        string? launchTicket = null;
        try
        {
            setStatus?.Invoke("Получение защитного билета запуска...");
            var hwid = HwidService.GetHwid();
            var ticketReq = new
            {
                username = account.Username,
                sessionKey = account.AccessToken,
                hwid = hwid
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var content = new StringContent(JsonSerializer.Serialize(ticketReq), Encoding.UTF8, "application/json");
            var resp = await http.PostAsync($"{cleanServerUrl}/api/launch/request-ticket", content, ct);

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ticket", out var tProp))
                {
                    launchTicket = tProp.GetString();
                    log?.Invoke($"Защитный билет запуска активирован: {launchTicket}");
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Предупреждение: ошибка получения билета: {ex.Message}");
        }

        return new IntegrityResult
        {
            Success = true,
            Ticket = launchTicket
        };
    }
}