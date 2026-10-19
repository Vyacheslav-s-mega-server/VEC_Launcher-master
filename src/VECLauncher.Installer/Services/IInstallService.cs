using System;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Installer.Models;

namespace VECLauncher.Installer.Services;

public interface IInstallService
{
    event Action<InstallProgressReport>? ProgressChanged;
    Task<bool> InstallAsync(InstallerConfig config, CancellationToken ct = default);
    Task<bool> UninstallAsync(InstallerConfig config, CancellationToken ct = default);
    void LaunchInstalledApp(string installDir);
    bool IsAppRunning();
    void TerminateRunningApp();
}
