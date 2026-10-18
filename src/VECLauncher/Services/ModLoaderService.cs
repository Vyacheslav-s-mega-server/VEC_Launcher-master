using System.Diagnostics;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class ModLoaderService
{
    private const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    private const string ForgeInstallerBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string NeoForgeInstallerBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    private readonly HttpClient _http;

    public ModLoaderService(HttpClient http) => _http = http;

    public GamePaths Paths { get; set; } = GamePaths.Shared;

    public string InstallRoot { get; set; } = LauncherPaths.Root;

    public event Action<string>? Status;
    private void Report(string s) { Status?.Invoke(s); Log.Info(s); }

    public async Task<string> InstallAsync(
        LoaderKind kind, string mcVersion, string loaderVersion,
        JavaInstallation java, CancellationToken ct = default)
    {
        return kind switch
        {
            LoaderKind.Fabric => await InstallFabricAsync(mcVersion, loaderVersion, ct).ConfigureAwait(false),
            LoaderKind.Forge => await InstallWithInstallerAsync(
                LoaderKind.Forge, mcVersion, loaderVersion, java, ct).ConfigureAwait(false),
            LoaderKind.NeoForge => await InstallWithInstallerAsync(
                LoaderKind.NeoForge, mcVersion, loaderVersion, java, ct).ConfigureAwait(false),
            _ => mcVersion
        };
    }

    private async Task<string> InstallFabricAsync(string mcVersion, string loaderVersion, CancellationToken ct)
    {
        Report($"Устанавливаю Fabric {loaderVersion} для {mcVersion}...");

        var url = $"{FabricMetaBase}/versions/loader/" +
                  $"{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";

        var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var versionId = doc.RootElement.GetProperty("id").GetString()
                        ?? $"fabric-loader-{loaderVersion}-{mcVersion}";

        var dir = Paths.VersionDir(versionId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Paths.VersionJson(versionId), json, ct).ConfigureAwait(false);

        Report($"Fabric установлен: {versionId}");
        return versionId;
    }

    private async Task<string> InstallWithInstallerAsync(
        LoaderKind kind, string mcVersion, string loaderVersion,
        JavaInstallation java, CancellationToken ct)
    {
        var name = kind.Display();
        Report($"Загружаю установщик {name} {loaderVersion}...");

        var (installerUrl, expectedId) = kind == LoaderKind.Forge
            ? ($"{ForgeInstallerBase}/{loaderVersion}/forge-{loaderVersion}-installer.jar",
                $"{mcVersion}-forge-{loaderVersion[(loaderVersion.IndexOf('-') + 1)..]}")
            : ($"{NeoForgeInstallerBase}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
                $"neoforge-{loaderVersion}");

        var existing = FindInstalledVersion(kind, mcVersion, loaderVersion);
        if (existing is not null)
        {
            Report($"{name} {loaderVersion} уже установлен ({existing}).");
            return existing;
        }

        Directory.CreateDirectory(LauncherPaths.CacheDir);
        var installerPath = Path.Combine(LauncherPaths.CacheDir, $"{kind}-{loaderVersion}-installer.jar");

        using (var resp = await _http.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Не удалось скачать установщик {name} {loaderVersion} (HTTP {(int)resp.StatusCode}). " +
                    "Возможно, эта версия недоступна для выбранной версии игры.");

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, true);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        EnsureLauncherProfiles();

        Report($"Запускаю установщик {name} (это может занять до минуты)...");

        if (!File.Exists(java.JavaConsoleExe))
        {
            throw new FileNotFoundException(
                $"Java не найдена: {java.JavaConsoleExe}. Установите Java {JavaService.RequiredJavaFor(mcVersion)}+ или скачайте через лаунчер.");
        }

        var psi = new ProcessStartInfo(java.JavaConsoleExe)
        {
            WorkingDirectory = InstallRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(InstallRoot);

        using var proc = new Process { StartInfo = psi };
        var output = new System.Text.StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Не удалось запустить установщик: {ex.Message}. Java: {java.JavaConsoleExe}");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var finished = await Task.Run(() => proc.WaitForExit(300_000), ct).ConfigureAwait(false);
        if (!finished)
        {
            try { proc.Kill(true); } catch (Exception ex) { Log.Warn(ex.Message); }
            throw new TimeoutException($"Установщик {name} не завершился за 5 минут.");
        }

        Log.Info($"ModLoaderService: {name} installer output:\n" + output);

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"Установщик {name} завершился с кодом {proc.ExitCode}. Подробности в launcher.log.");

        try { File.Delete(installerPath); } catch (Exception ex) { Log.Warn(ex.Message); }

        var installed = FindInstalledVersion(kind, mcVersion, loaderVersion)
                        ?? (Directory.Exists(Paths.VersionDir(expectedId)) ? expectedId : null);

        if (installed is null)
            throw new InvalidOperationException(
                $"{name} установлен, но профиль версии не найден. Проверьте папку versions.");

        Report($"{name} установлен: {installed}");
        return installed;
    }

    private string? FindInstalledVersion(LoaderKind kind, string mcVersion, string loaderVersion)
    {
        if (!Directory.Exists(Paths.VersionsDir)) return null;

        var shortVer = loaderVersion.Contains('-')
            ? loaderVersion[(loaderVersion.IndexOf('-') + 1)..]
            : loaderVersion;

        var token = kind == LoaderKind.Forge ? "forge" : "neoforge";

        foreach (var dir in Directory.GetDirectories(Paths.VersionsDir))
        {
            var id = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, id + ".json"))) continue;

            var lower = id.ToLowerInvariant();
            if (!lower.Contains(token, StringComparison.Ordinal)) continue;
            if (kind == LoaderKind.Forge && lower.Contains("neoforge", StringComparison.Ordinal)) continue;
            if (!lower.Contains(shortVer.ToLowerInvariant(), StringComparison.Ordinal)) continue;

            return id;
        }

        return null;
    }

    private void EnsureLauncherProfiles()
    {
        var path = Path.Combine(InstallRoot, "launcher_profiles.json");
        if (File.Exists(path)) return;

        Directory.CreateDirectory(InstallRoot);
        File.WriteAllText(path, """
            {
              "profiles": {},
              "settings": {},
              "version": 3
            }
            """);
    }
}