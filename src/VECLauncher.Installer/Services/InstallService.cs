using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Installer.Models;

namespace VECLauncher.Installer.Services;

public sealed class InstallService : IInstallService
{
    private readonly HttpClient _http;
    private readonly IUpdateCheckService _updateCheck;
    private readonly IShortcutService _shortcuts;
    private readonly IRegistryService _registry;

    private static string CurrentProcessPath =>
        Process.GetCurrentProcess().MainModule?.FileName ??
        System.Reflection.Assembly.GetExecutingAssembly().Location;

    public event Action<InstallProgressReport>? ProgressChanged;

    public InstallService(
        HttpClient http,
        IUpdateCheckService updateCheck,
        IShortcutService shortcuts,
        IRegistryService registry)
    {
        _http = http;
        _updateCheck = updateCheck;
        _shortcuts = shortcuts;
        _registry = registry;
    }

    public bool IsAppRunning()
    {
        return Process.GetProcessesByName("VECLauncher").Length > 0;
    }

    public void TerminateRunningApp()
    {
        foreach (var proc in Process.GetProcessesByName("VECLauncher"))
        {
            try
            {
                proc.CloseMainWindow();
                if (!proc.WaitForExit(2000))
                {
                    proc.Kill();
                }
            }
            catch { }
        }
    }

    public async Task<bool> InstallAsync(InstallerConfig config, CancellationToken ct = default)
    {
        var targetDir = config.InstallPath;
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"VECLauncher_Setup_{Guid.NewGuid():N}.zip");

        try
        {
            Report(InstallStage.CheckingUpdate, "Поиск последней версии VEC Launcher...", 5);

            if (IsAppRunning())
            {
                Report(InstallStage.CheckingUpdate, "Закрытие работающего VEC Launcher...", 10);
                TerminateRunningApp();
                await Task.Delay(500, ct);
            }

            var release = await _updateCheck.GetLatestReleaseAsync(ct);
            if (string.IsNullOrEmpty(release.DownloadUrl))
            {
                throw new InvalidOperationException("Не удалось получить ссылку на загрузку лаунчера.");
            }

            Report(InstallStage.Downloading, "Подключение к серверу загрузки...", 15);

            var candidateUrls = new System.Collections.Generic.List<string> { release.DownloadUrl };

            if (Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var baseUri))
            {
                var path = baseUri.PathAndQuery;
                if (!baseUri.IsLoopback)
                {
                    candidateUrls.Add($"http://localhost:5044{path}");
                    candidateUrls.Add("http://localhost:5044/downloads/VECLauncher-win-x64.zip");
                    candidateUrls.Add("http://localhost:5044/downloads/VECLauncher.exe");
                }
                else
                {
                    candidateUrls.Add($"https://vec.kpvk.edu.kz{path}");
                }

                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    candidateUrls.Add(new Uri(baseUri, "/downloads/VECLauncher.exe").ToString());
                }
                else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    candidateUrls.Add(new Uri(baseUri, "/downloads/VECLauncher-win-x64.zip").ToString());
                }
            }

            var downloaded = false;
            Exception? lastEx = null;

            foreach (var url in System.Linq.Enumerable.Distinct(candidateUrls))
            {
                try
                {
                    await DownloadFileWithProgressAsync(url, tempZipPath, ct);
                    downloaded = true;
                    release.DownloadUrl = url;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastEx = ex;
                }
            }

            if (!downloaded)
            {
                throw lastEx ?? new InvalidOperationException("Не удалось загрузить пакет лаунчера.");
            }

            if (!string.IsNullOrEmpty(release.Sha256))
            {
                Report(InstallStage.Downloading, "Проверка целостности загруженного пакета...", 75);
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes;
                    using (var stream = File.OpenRead(tempZipPath))
                    {
                        hashBytes = await Task.Run(() => sha256.ComputeHash(stream), ct).ConfigureAwait(false);
                    }
                    var hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    if (!string.Equals(hashStr, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Ошибка проверки целостности файла (не совпадает SHA-256).");
                    }
                }
            }

            Report(InstallStage.Extracting, "Подготовка файлов лаунчера...", 80);
            Directory.CreateDirectory(targetDir);

            var installedExe = Path.Combine(targetDir, "VECLauncher.exe");

            await Task.Run(() =>
            {
                if (release.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(tempZipPath, installedExe, true);
                }
                else
                {
                    try
                    {
                        using var archive = ZipFile.OpenRead(tempZipPath);
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                var folderPath = Path.Combine(targetDir, entry.FullName);
                                Directory.CreateDirectory(folderPath);
                                continue;
                            }

                            var destinationPath = Path.Combine(targetDir, entry.FullName);
                            var entryDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(entryDir))
                            {
                                Directory.CreateDirectory(entryDir);
                            }

                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                    catch (InvalidDataException)
                    {
                        File.Copy(tempZipPath, installedExe, true);
                    }
                }
            }, ct);

            // Copy setup executable itself into install directory for uninstallation / auto-updates
            try
            {
                var currentSetupPath = CurrentProcessPath;
                if (!string.IsNullOrEmpty(currentSetupPath) && File.Exists(currentSetupPath))
                {
                    var targetSetupPath = Path.Combine(targetDir, "VEC_Setup.exe");
                    if (!string.Equals(currentSetupPath, targetSetupPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(currentSetupPath, targetSetupPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to copy setup to target dir: {ex.Message}");
            }

            Report(InstallStage.CreatingShortcuts, "Создание ярлыков...", 90);

            if (config.CreateDesktopShortcut)
            {
                _shortcuts.CreateShortcut(
                    installedExe,
                    _shortcuts.DesktopShortcutPath,
                    "VEC Launcher — Официальный лаунчер Minecraft сервера VEC");
            }

            if (config.CreateStartMenuShortcut)
            {
                _shortcuts.CreateShortcut(
                    installedExe,
                    _shortcuts.StartMenuShortcutPath,
                    "VEC Launcher — Официальный лаунчер Minecraft сервера VEC");
            }

            Report(InstallStage.Registering, "Регистрация в системе...", 95);

            if (config.RegisterProtocol)
            {
                _registry.RegisterProtocol(installedExe);
            }

            var setupInTarget = Path.Combine(targetDir, "VEC_Setup.exe");
            _registry.RegisterUninstall(targetDir, installedExe, setupInTarget, release.Version);

            Report(InstallStage.Completed, "Установка успешно завершена!", 100);

            if (config.LaunchAfterInstall && config.IsSilent)
            {
                LaunchInstalledApp(targetDir);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportError(ex.Message);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            }
            catch { }
        }
    }

    public async Task<bool> UninstallAsync(InstallerConfig config, CancellationToken ct = default)
    {
        try
        {
            Report(InstallStage.Uninstalling, "Остановка запущенных процессов...", 10);
            TerminateRunningApp();
            await Task.Delay(500, ct);

            Report(InstallStage.Uninstalling, "Удаление ярлыков и записей реестра...", 30);
            _shortcuts.RemoveShortcut(_shortcuts.DesktopShortcutPath);
            _shortcuts.RemoveShortcut(_shortcuts.StartMenuShortcutPath);
            _registry.UnregisterProtocol();
            _registry.UnregisterUninstall();

            Report(InstallStage.Uninstalling, "Удаление данных лаунчера и кэша...", 60);
            var roamingDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".veclauncher");
            if (Directory.Exists(roamingDataDir))
            {
                try
                {
                    Directory.Delete(roamingDataDir, true);
                }
                catch { }
            }

            Report(InstallStage.Uninstalling, "Удаление файлов программы...", 80);
            var targetDir = config.InstallPath;
            if (Directory.Exists(targetDir))
            {
                // Delete files except the currently running setup executable
                foreach (var file in Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (!string.Equals(file, CurrentProcessPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }

                // Delete all empty subdirectories in targetDir
                foreach (var dir in Directory.GetDirectories(targetDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // Schedule deletion of remaining dir and setup exe via powershell once the process exits
                var currentPid = Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"try {{ Wait-Process -Id {currentPid} -Timeout 30 -ErrorAction SilentlyContinue }} catch {{}}; Start-Sleep -Milliseconds 500; Remove-Item -LiteralPath '{targetDir}' -Recurse -Force -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath '{roamingDataDir}') {{ Remove-Item -LiteralPath '{roamingDataDir}' -Recurse -Force -ErrorAction SilentlyContinue }}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }

            Report(InstallStage.Completed, "VEC Launcher успешно удален с вашего компьютера.", 100);
            return true;
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
            return false;
        }
    }

    public void LaunchInstalledApp(string installDir)
    {
        var exe = Path.Combine(installDir, "VECLauncher.exe");
        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }
    }

    private async Task DownloadFileWithProgressAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
        using (var contentStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            var stopwatch = Stopwatch.StartNew();
            var lastReport = Stopwatch.StartNew();
            long lastReadBytes = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                totalRead += bytesRead;

                if (lastReport.ElapsedMilliseconds >= 100)
                {
                    var deltaBytes = totalRead - lastReadBytes;
                    var deltaSec = lastReport.Elapsed.TotalSeconds;
                    var speedBytesPerSec = deltaSec > 0 ? (deltaBytes / deltaSec) : 0;
                    var speedFormatted = $"{InstallProgressReport.FormatBytes((long)speedBytesPerSec)}/с";

                    double percent;
                    string msg;

                    if (totalBytes > 0)
                    {
                        var dlPercent = (double)totalRead / totalBytes;
                        percent = 15.0 + dlPercent * 60.0; // 15% to 75%
                        msg = $"Загрузка: {InstallProgressReport.FormatBytes(totalRead)} из {InstallProgressReport.FormatBytes(totalBytes)} ({speedFormatted})";
                    }
                    else
                    {
                        percent = 40.0;
                        msg = $"Загрузка: {InstallProgressReport.FormatBytes(totalRead)} ({speedFormatted})";
                    }

                    Report(InstallStage.Downloading, msg, percent, speedFormatted, totalRead, totalBytes);

                    lastReadBytes = totalRead;
                    lastReport.Restart();
                }
            }
        }
    }

    private void Report(
        InstallStage stage,
        string msg,
        double percent,
        string speed = "",
        long downloaded = 0,
        long total = 0)
    {
        ProgressChanged?.Invoke(new InstallProgressReport
        {
            Stage = stage,
            StatusMessage = msg,
            Percent = Math.Max(0.0, Math.Min(100.0, percent)),
            SpeedFormatted = speed,
            DownloadedBytes = downloaded,
            TotalBytes = total
        });
    }

    private void ReportError(string error)
    {
        ProgressChanged?.Invoke(new InstallProgressReport
        {
            Stage = InstallStage.Error,
            StatusMessage = "Произошла ошибка при установке.",
            ErrorMessage = error,
            Percent = 0
        });
    }
}
