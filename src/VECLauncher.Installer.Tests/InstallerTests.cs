using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Installer.Models;
using VECLauncher.Installer.Services;
using VECLauncher.Installer.ViewModels;
using Xunit;

namespace VECLauncher.Installer.Tests;

public class InstallerTests
{
    [Fact]
    public void InstallerConfig_FromArgs_DefaultValues()
    {
        var config = InstallerConfig.FromArgs(Array.Empty<string>());

        Assert.False(config.IsSilent);
        Assert.False(config.IsUpdate);
        Assert.False(config.IsUninstall);
        Assert.True(config.CreateDesktopShortcut);
        Assert.True(config.CreateStartMenuShortcut);
        Assert.False(config.LaunchAfterInstall);
        Assert.Contains("Programs", config.InstallPath);
        Assert.Contains("VECLauncher", config.InstallPath);
    }

    [Theory]
    [InlineData("--silent")]
    [InlineData("-s")]
    [InlineData("/s")]
    public void InstallerConfig_FromArgs_ParsesSilentFlag(string arg)
    {
        var config = InstallerConfig.FromArgs(new[] { arg });
        Assert.True(config.IsSilent);
    }

    [Theory]
    [InlineData("--update")]
    [InlineData("-u")]
    [InlineData("/u")]
    public void InstallerConfig_FromArgs_ParsesUpdateFlag(string arg)
    {
        var config = InstallerConfig.FromArgs(new[] { arg });
        Assert.True(config.IsUpdate);
    }

    [Fact]
    public void InstallerConfig_FromArgs_ParsesUninstallFlag()
    {
        var config = InstallerConfig.FromArgs(new[] { "--uninstall" });
        Assert.True(config.IsUninstall);
    }

    [Fact]
    public void InstallerConfig_FromArgs_ParsesCustomInstallDir()
    {
        var targetDir = @"D:\Games\VECLauncher";
        var config = InstallerConfig.FromArgs(new[] { $"--dir={targetDir}" });
        Assert.Equal(targetDir, config.InstallPath);
    }

    [Fact]
    public void InstallerConfig_FromArgs_ParsesShortcutAndLaunchFlags()
    {
        var config = InstallerConfig.FromArgs(new[]
        {
            "--no-desktop-shortcut",
            "--no-start-shortcut",
            "--no-launch"
        });

        Assert.False(config.CreateDesktopShortcut);
        Assert.False(config.CreateStartMenuShortcut);
        Assert.False(config.LaunchAfterInstall);
    }

    [Fact]
    public void InstallProgressReport_FormatBytes_FormatsCorrectUnits()
    {
        var b = InstallProgressReport.FormatBytes(500);
        Assert.Equal("500 Б", b);

        var kb = InstallProgressReport.FormatBytes(1024 * 50);
        Assert.Equal("50 КБ", kb);

        var mb = InstallProgressReport.FormatBytes(1024 * 1024 * 25);
        Assert.Equal("25 МБ", mb);
    }

    [Fact]
    public void ReleaseManifest_JsonSerialization_Roundtrip()
    {
        var manifest = new ReleaseManifest
        {
            Version = "1.2.3",
            DownloadUrl = "https://vec.kpvk.edu.kz/downloads/VECLauncher-win-x64.zip",
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Changelog = "Modern Bootstrap Installer",
            FileSize = 52428800
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<ReleaseManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(manifest.Version, deserialized.Version);
        Assert.Equal(manifest.DownloadUrl, deserialized.DownloadUrl);
        Assert.Equal(manifest.Sha256, deserialized.Sha256);
        Assert.Equal(manifest.Changelog, deserialized.Changelog);
        Assert.Equal(manifest.FileSize, deserialized.FileSize);
    }

    private class MockInstallService : IInstallService
    {
        public event Action<InstallProgressReport>? ProgressChanged;
        public bool IsRunning { get; set; }
        public bool InstallResult { get; set; } = true;
        public bool UninstallResult { get; set; } = true;
        public bool AppLaunched { get; set; }

        public Task<bool> InstallAsync(InstallerConfig config, CancellationToken ct = default)
        {
            ProgressChanged?.Invoke(new InstallProgressReport
            {
                Stage = InstallStage.Completed,
                StatusMessage = "Готово",
                Percent = 100
            });
            return Task.FromResult(InstallResult);
        }

        public Task<bool> UninstallAsync(InstallerConfig config, CancellationToken ct = default)
        {
            ProgressChanged?.Invoke(new InstallProgressReport
            {
                Stage = InstallStage.Completed,
                StatusMessage = "Удалено",
                Percent = 100
            });
            return Task.FromResult(UninstallResult);
        }

        public void LaunchInstalledApp(string installDir) => AppLaunched = true;
        public bool IsAppRunning() => IsRunning;
        public void TerminateRunningApp() => IsRunning = false;
    }

    [Fact]
    public void InstallerViewModel_InitialState_IsWelcomeStep()
    {
        var mock = new MockInstallService();
        var config = InstallerConfig.FromArgs(Array.Empty<string>());
        var vm = new InstallerViewModel(mock, config);

        Assert.True(vm.IsWelcomeStep);
        Assert.False(vm.IsInstallingStep);
        Assert.False(vm.IsFinishedStep);
        Assert.False(vm.IsErrorStep);
        Assert.False(vm.IsUninstallStep);
        Assert.True(vm.CanGoNext);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void InstallerViewModel_WizardNavigation_ForwardAndBack()
    {
        var mock = new MockInstallService();
        var config = InstallerConfig.FromArgs(Array.Empty<string>());
        var vm = new InstallerViewModel(mock, config);

        Assert.Equal(WizardStep.Welcome, vm.Step);

        // Next -> Directory
        vm.NextStep();
        Assert.Equal(WizardStep.Directory, vm.Step);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanGoNext);

        // Next -> Confirmation
        vm.NextStep();
        Assert.Equal(WizardStep.Confirmation, vm.Step);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanInstall);

        // Back -> Directory
        vm.BackStep();
        Assert.Equal(WizardStep.Directory, vm.Step);

        // Back -> Welcome
        vm.BackStep();
        Assert.Equal(WizardStep.Welcome, vm.Step);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public async Task InstallerViewModel_StartInstall_TransitionsToFinished()
    {
        var mock = new MockInstallService();
        var config = InstallerConfig.FromArgs(Array.Empty<string>());
        var vm = new InstallerViewModel(mock, config);

        await vm.StartInstallAsync();

        Assert.Equal(InstallStage.Completed, vm.Stage);
        Assert.True(vm.IsFinishedStep);
        Assert.False(vm.IsWelcomeStep);
        Assert.True(vm.ShowFinishButton);
    }

    [Fact]
    public async Task InstallerViewModel_StartUninstall_TransitionsToUninstallFinished()
    {
        var mock = new MockInstallService();
        var config = InstallerConfig.FromArgs(new[] { "--uninstall" });
        var vm = new InstallerViewModel(mock, config);

        Assert.True(vm.IsUninstallStep);
        Assert.True(vm.ShowUninstallButton);

        await vm.StartUninstallAsync();

        Assert.True(vm.IsUninstallFinishedStep);
        Assert.True(vm.ShowFinishButton);
        Assert.Equal("Закрыть", vm.FinishButtonText);
    }
}
