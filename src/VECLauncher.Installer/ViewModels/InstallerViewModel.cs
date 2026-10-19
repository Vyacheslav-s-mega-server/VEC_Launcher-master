using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VECLauncher.Installer.Models;
using VECLauncher.Installer.Services;

namespace VECLauncher.Installer.ViewModels;

public partial class InstallerViewModel : ObservableObject
{
    private readonly IInstallService _install;
    private CancellationTokenSource? _cts;

    public InstallerConfig Config { get; }

    [ObservableProperty]
    private InstallStage _stage = InstallStage.Idle;

    [ObservableProperty]
    private string _statusMessage = "Готов к установке";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _speedDisplay = "";

    [ObservableProperty]
    private string _errorDetails = "";

    [ObservableProperty]
    private bool _isOptionsExpanded;

    [ObservableProperty]
    private string _installPath;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    [ObservableProperty]
    private bool _launchAfterInstall = true;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsWelcomeStep => Stage == InstallStage.Idle && !Config.IsUninstall;
    public bool IsInstallingStep => Stage is InstallStage.CheckingUpdate or InstallStage.Downloading or InstallStage.Extracting or InstallStage.CreatingShortcuts or InstallStage.Registering or InstallStage.Finalizing or InstallStage.Uninstalling;
    public bool IsCompletedStep => Stage == InstallStage.Completed;
    public bool IsErrorStep => Stage == InstallStage.Error;
    public bool IsUninstallStep => Config.IsUninstall && Stage == InstallStage.Idle;

    public Action? RequestClose { get; set; }

    public InstallerViewModel(IInstallService install, InstallerConfig config)
    {
        _install = install;
        Config = config;

        _installPath = config.InstallPath;
        _createDesktopShortcut = config.CreateDesktopShortcut;
        _createStartMenuShortcut = config.CreateStartMenuShortcut;
        _launchAfterInstall = config.LaunchAfterInstall;

        _install.ProgressChanged += OnProgressChanged;

        if (Config.IsSilent || Config.IsUpdate)
        {
            _ = Task.Run(StartInstallAsync);
        }
    }

    private void OnProgressChanged(InstallProgressReport report)
    {
        void Update()
        {
            Stage = report.Stage;
            StatusMessage = report.StatusMessage;
            ProgressPercent = report.Percent;
            SpeedDisplay = report.SpeedFormatted;

            if (!string.IsNullOrEmpty(report.ErrorMessage))
            {
                ErrorDetails = report.ErrorMessage;
            }

            NotifyStepChanges();
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    private void NotifyStepChanges()
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsInstallingStep));
        OnPropertyChanged(nameof(IsCompletedStep));
        OnPropertyChanged(nameof(IsErrorStep));
        OnPropertyChanged(nameof(IsUninstallStep));
    }

    [RelayCommand]
    public void ToggleOptions()
    {
        IsOptionsExpanded = !IsOptionsExpanded;
    }

    [RelayCommand]
    public void BrowsePath()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку для установки VEC Launcher",
            InitialDirectory = Directory.Exists(InstallPath) ? InstallPath : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (dlg.ShowDialog() == true)
        {
            InstallPath = dlg.FolderName;
        }
    }

    [RelayCommand]
    public async Task StartInstallAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        Config.InstallPath = InstallPath;
        Config.CreateDesktopShortcut = CreateDesktopShortcut;
        Config.CreateStartMenuShortcut = CreateStartMenuShortcut;
        Config.LaunchAfterInstall = LaunchAfterInstall;

        _cts = new CancellationTokenSource();
        Stage = InstallStage.CheckingUpdate;
        NotifyStepChanges();

        var success = await _install.InstallAsync(Config, _cts.Token);
        IsBusy = false;

        if (success && Config.IsSilent)
        {
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    public async Task StartUninstallAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        _cts = new CancellationTokenSource();
        Stage = InstallStage.Uninstalling;
        NotifyStepChanges();

        var success = await _install.UninstallAsync(Config, _cts.Token);
        IsBusy = false;

        if (success && Config.IsSilent)
        {
            RequestClose?.Invoke();
        }
    }

    [RelayCommand]
    public void LaunchApp()
    {
        _install.LaunchInstalledApp(Config.InstallPath);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        RequestClose?.Invoke();
    }
}
