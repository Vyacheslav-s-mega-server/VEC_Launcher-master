using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Installer.Models;
using VECLauncher.Installer.Services;

namespace VECLauncher.Installer.ViewModels;

public enum WizardStep
{
    Welcome = 0,
    Directory = 1,
    Confirmation = 2,
    Installing = 3,
    Finished = 4,
    Error = 5,
    Uninstall = 6,
    UninstallFinished = 7
}

public partial class InstallerViewModel : ObservableObject
{
    private readonly IInstallService _install;
    private CancellationTokenSource? _cts;

    public InstallerConfig Config { get; }

    [ObservableProperty]
    private WizardStep _step = WizardStep.Welcome;

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
    private string _installPath;

    [ObservableProperty]
    private bool _createDesktopShortcut = true;

    [ObservableProperty]
    private bool _createStartMenuShortcut = true;

    [ObservableProperty]
    private bool _launchAfterInstall = false;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsWelcomeStep => Step == WizardStep.Welcome;
    public bool IsDirectoryStep => Step == WizardStep.Directory;
    public bool IsConfirmationStep => Step == WizardStep.Confirmation;
    public bool IsInstallingStep => Step == WizardStep.Installing;
    public bool IsFinishedStep => Step == WizardStep.Finished;
    public bool IsErrorStep => Step == WizardStep.Error;
    public bool IsUninstallStep => Step == WizardStep.Uninstall;
    public bool IsUninstallFinishedStep => Step == WizardStep.UninstallFinished;

    public bool CanGoBack => (Step == WizardStep.Directory || Step == WizardStep.Confirmation) && !IsBusy;
    public bool CanGoNext => (Step == WizardStep.Welcome || Step == WizardStep.Directory) && !IsBusy;
    public bool CanInstall => Step == WizardStep.Confirmation && !IsBusy;
    public bool CanCancel => Step != WizardStep.Finished && Step != WizardStep.UninstallFinished;

    public bool ShowBackButton => Step == WizardStep.Directory || Step == WizardStep.Confirmation;
    public bool ShowNextButton => Step == WizardStep.Welcome || Step == WizardStep.Directory;
    public bool ShowInstallButton => Step == WizardStep.Confirmation;
    public bool ShowUninstallButton => Step == WizardStep.Uninstall;
    public bool ShowFinishButton => Step == WizardStep.Finished || Step == WizardStep.UninstallFinished;
    public bool ShowCancelButton => Step != WizardStep.Finished && Step != WizardStep.UninstallFinished;

    public string FinishButtonText => Step == WizardStep.UninstallFinished ? "Закрыть" : "Готово";
    public string InstallingTitle => Config.IsUninstall ? "Удаление VEC Launcher" : "Установка VEC Launcher";

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

        if (Config.IsUninstall)
        {
            _step = WizardStep.Uninstall;
            if (Config.IsSilent)
            {
                _ = Task.Run(StartUninstallAsync);
            }
        }
        else if (Config.IsSilent || Config.IsUpdate)
        {
            _step = WizardStep.Installing;
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
                ErrorDetails = report.ErrorMessage ?? string.Empty;
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
        OnPropertyChanged(nameof(IsDirectoryStep));
        OnPropertyChanged(nameof(IsConfirmationStep));
        OnPropertyChanged(nameof(IsInstallingStep));
        OnPropertyChanged(nameof(IsFinishedStep));
        OnPropertyChanged(nameof(IsErrorStep));
        OnPropertyChanged(nameof(IsUninstallStep));
        OnPropertyChanged(nameof(IsUninstallFinishedStep));

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanCancel));

        OnPropertyChanged(nameof(ShowBackButton));
        OnPropertyChanged(nameof(ShowNextButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowUninstallButton));
        OnPropertyChanged(nameof(ShowFinishButton));
        OnPropertyChanged(nameof(ShowCancelButton));
        OnPropertyChanged(nameof(FinishButtonText));
        OnPropertyChanged(nameof(InstallingTitle));
    }

    [RelayCommand]
    public void NextStep()
    {
        if (Step == WizardStep.Welcome)
        {
            Step = WizardStep.Directory;
        }
        else if (Step == WizardStep.Directory)
        {
            if (string.IsNullOrWhiteSpace(InstallPath))
            {
                InstallPath = InstallerConfig.DefaultInstallPath;
            }
            Step = WizardStep.Confirmation;
        }
        NotifyStepChanges();
    }

    [RelayCommand]
    public void BackStep()
    {
        if (Step == WizardStep.Confirmation)
        {
            Step = WizardStep.Directory;
        }
        else if (Step == WizardStep.Directory)
        {
            Step = WizardStep.Welcome;
        }
        NotifyStepChanges();
    }

    [RelayCommand]
    public void BrowsePath()
    {
        using (var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для установки VEC Launcher",
            SelectedPath = Directory.Exists(InstallPath) ? InstallPath : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ShowNewFolderButton = true
        })
        {
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                InstallPath = dlg.SelectedPath;
            }
        }
    }

    [RelayCommand]
    public async Task StartInstallAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Step = WizardStep.Installing;
        NotifyStepChanges();

        Config.InstallPath = InstallPath;
        Config.CreateDesktopShortcut = CreateDesktopShortcut;
        Config.CreateStartMenuShortcut = CreateStartMenuShortcut;
        Config.LaunchAfterInstall = LaunchAfterInstall;

        _cts = new CancellationTokenSource();

        try
        {
            var success = await _install.InstallAsync(Config, _cts.Token);
            IsBusy = false;
            Step = success ? WizardStep.Finished : WizardStep.Error;
            NotifyStepChanges();
        }
        catch (OperationCanceledException)
        {
            IsBusy = false;
            Step = WizardStep.Welcome;
            NotifyStepChanges();
        }
        catch (Exception ex)
        {
            IsBusy = false;
            ErrorDetails = ex.Message;
            Step = WizardStep.Error;
            NotifyStepChanges();
        }
    }

    [RelayCommand]
    public async Task StartUninstallAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Step = WizardStep.Installing;
        NotifyStepChanges();

        _cts = new CancellationTokenSource();

        try
        {
            var success = await _install.UninstallAsync(Config, _cts.Token);
            IsBusy = false;
            Step = success ? WizardStep.UninstallFinished : WizardStep.Error;
            NotifyStepChanges();
        }
        catch (Exception ex)
        {
            IsBusy = false;
            ErrorDetails = ex.Message;
            Step = WizardStep.Error;
            NotifyStepChanges();
        }
    }

    [RelayCommand]
    public void Finish()
    {
        if (Step == WizardStep.Finished && LaunchAfterInstall)
        {
            try
            {
                _install.LaunchInstalledApp(InstallPath);
            }
            catch { }
        }
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void RetryInstall()
    {
        Step = Config.IsUninstall ? WizardStep.Uninstall : WizardStep.Confirmation;
        NotifyStepChanges();
    }

    [RelayCommand]
    public void Cancel()
    {
        if (Step == WizardStep.Installing && IsBusy)
        {
            var title = Config.IsUninstall ? "Отмена удаления" : "Отмена установки";
            var text = Config.IsUninstall
                ? "Вы уверены, что хотите отменить удаление?"
                : "Вы уверены, что хотите прервать установку VEC Launcher?";

            var res = MessageBox.Show(
                text,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _cts?.Cancel();
                RequestClose?.Invoke();
            }
            return;
        }

        RequestClose?.Invoke();
    }
}
