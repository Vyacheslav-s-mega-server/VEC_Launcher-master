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
                    proc.Kill(true);
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

            await DownloadFileWithProgressAsync(release.DownloadUrl, tempZipPath, ct);

            if (!string.IsNullOrEmpty(release.Sha256))
            {
                Report(InstallStage.Downloading, "Проверка целостности загруженного пакета...", 75);
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(tempZipPath);
                var hashBytes = await sha256.ComputeHashAsync(stream, ct);
                var hashStr = Convert.ToHexString(hashBytes);

                if (!string.Equals(hashStr, release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Ошибка проверки целостности файла (не совпадает SHA-256).");
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
                var currentSetupPath = Environment.ProcessPath;
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

            if (config.LaunchAfterInstall && !config.IsSilent)
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

            Report(InstallStage.Uninstalling, "Удаление ярлыков и записей реестра...", 40);
            _shortcuts.RemoveShortcut(_shortcuts.DesktopShortcutPath);
            _shortcuts.RemoveShortcut(_shortcuts.StartMenuShortcutPath);
            _registry.UnregisterProtocol();
            _registry.UnregisterUninstall();

            Report(InstallStage.Uninstalling, "Удаление файлов лаунчера...", 70);

            var targetDir = config.InstallPath;
            if (Directory.Exists(targetDir))
            {
                // Delete files except the currently running setup executable
                foreach (var file in Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (!string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }

                // Schedule deletion of remaining dir via cmd after exit
                var currentExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{targetDir}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
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
        await using var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        var stopwatch = Stopwatch.StartNew();
        var lastReport = Stopwatch.StartNew();
        long lastReadBytes = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
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
            Percent = Math.Clamp(percent, 0, 100),
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
