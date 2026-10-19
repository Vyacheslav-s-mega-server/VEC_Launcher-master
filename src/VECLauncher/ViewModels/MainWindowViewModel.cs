using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public readonly VersionService Versions;
    public readonly DownloadManager Downloads;
    public readonly JavaService Java;
    public readonly SkinService Skins;
    public readonly GameLauncher Game;
    public readonly ModLoaderService Loaders;
    public readonly ServerPingService Ping;

    public readonly GameSessionManager Sessions = new();
    public readonly GameStatistics Stats;
    public readonly LaunchService Launch;
    public readonly IAccountRepository Accounts;
    public IDialogService Dialogs { get; private set; } = null!;
    public IInstanceService? InstanceService { get; }
    public IMaintenanceService? Maintenance { get; }
    public IImageCacheService? ImageCache { get; }
    public readonly ICapeService Capes;
    public readonly IContentService? Content;

    public static bool IsInternalSkinWrite { get; set; }

    [ObservableProperty]
    private string _totalPlayTimeDisplay = "0 ч";

    [ObservableProperty]
    private string _serverPlayersDisplay = "— / —";

    [ObservableProperty]
    private ObservableCollection<MinecraftAccount> _savedAccounts = new();

    public MainWindowViewModel(
        VersionService versions,
        DownloadManager downloads,
        JavaService java,
        SkinService skins,
        GameLauncher game,
        ModLoaderService loaders,
        ServerPingService ping,
        LaunchService launch,
        GameStatistics stats,
        IAccountRepository accounts,
        IDialogService? dialogs = null,
        IInstanceService? instanceService = null,
        LauncherSettings? settings = null,
        IMaintenanceService? maintenance = null,
        IImageCacheService? imageCache = null,
        ICapeService? capes = null,
        IContentService? content = null)
    {
        Versions = versions;
        Downloads = downloads;
        Java = java;
        Skins = skins;
        Game = game;
        Loaders = loaders;
        Ping = ping;
        Stats = stats;
        Accounts = accounts;
        Launch = launch;
        Capes = capes ?? new CapeService(new HttpClient());
        Content = content ?? (instanceService != null ? new ContentService(new HttpClient(), instanceService) : null);
        if (dialogs != null) Dialogs = dialogs;
        InstanceService = instanceService;
        if (settings != null) Settings = settings;
        else Settings.PropertyChanged += OnSettingsPropertyChanged;
        Maintenance = maintenance;
        ImageCache = imageCache;

        TotalPlayTimeDisplay = Stats.GetFormattedTotalTime();
        Sessions.SessionExited += (session, code) => HandleSessionExited(session, code);
    }

    public void SetDialogService(IDialogService dialogs)
    {
        Dialogs = dialogs;
    }

    public enum PageId { Home, Content, Account, Settings }

    [ObservableProperty]
    private PageId _activePage = PageId.Home;

    [ObservableProperty]
    private int _activeNavIndex;

    [RelayCommand]
    private void Navigate(string tag)
    {
        ActivePage = tag switch
        {
            "0" => PageId.Home,
            "3" => PageId.Account,
            "4" => PageId.Settings,
            "7" => PageId.Content,
            _ => ActivePage
        };

        ActiveNavIndex = tag switch
        {
            "0" => 0,
            "7" => 1,
            "3" => 2,
            "4" => 3,
            _ => ActiveNavIndex
        };
    }

    [ObservableProperty]
    private MinecraftAccount? _account;

    [ObservableProperty]
    private string _sideName = "Не выполнен вход";

    [ObservableProperty]
    private string _sideStatus = "Оффлайн";

    [ObservableProperty]
    private bool _isAccountLoggedIn;

    public bool IsAccountLoggedOut => !IsAccountLoggedIn;

    partial void OnIsAccountLoggedInChanged(bool value) => OnPropertyChanged(nameof(IsAccountLoggedOut));

    [ObservableProperty]
    private string _accountUsernameDisplay = "—";

    [ObservableProperty]
    private string _minecraftNickDisplay = "ID: —";

    [ObservableProperty]
    private bool _hasMinecraftLicense;

    [ObservableProperty]
    private ImageSource? _currentAvatarImage;

    [ObservableProperty]
    private bool _isSavedAccountsPopupOpen;

    [ObservableProperty]
    private bool _hasSavedAccounts;

    public bool HasNoSavedAccounts => !HasSavedAccounts;

    partial void OnHasSavedAccountsChanged(bool value) => OnPropertyChanged(nameof(HasNoSavedAccounts));

    [ObservableProperty]
    private ObservableCollection<AccountItemViewModel> _savedAccountItems = new();

    public event Action<MinecraftAccount?, bool>? AccountChanged;

    public void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        Account = acc;
        IsAccountLoggedIn = true;
        AccountUsernameDisplay = acc.Username;
        HasMinecraftLicense = acc.HasMinecraftLicense;
        MinecraftNickDisplay = acc.HasMinecraftLicense
            ? (!string.IsNullOrEmpty(acc.MicrosoftGamertag) ? $"Minecraft: {acc.MicrosoftGamertag}" : $"Minecraft: {acc.Username}")
            : $"ID: {acc.Username}";

        if (acc.IsVec)
        {
            SideName = acc.Username;
            SideStatus = "VEC ID";
        }
        else if (acc.IsOffline)
        {
            SideName = acc.Username;
            SideStatus = "Оффлайн";
        }
        else
        {
            SideName = acc.Username;
            SideStatus = "Microsoft";
        }

        var localSkin = SkinService.FindAccountSkin(acc.Username);
        if (localSkin != null && File.Exists(localSkin))
        {
            try
            {
                var bytes = File.ReadAllBytes(localSkin);
                CurrentAvatarImage = SkinService.CreateCrispHeadAvatar(bytes, 48);
            }
            catch { }
        }

        RefreshSavedAccounts();
        AccountChanged?.Invoke(acc, refreshSkin);
    }

    public void ClearAccount()
    {
        Account = null;
        SideName = "Не выполнен вход";
        SideStatus = "Оффлайн";
        IsAccountLoggedIn = false;
        AccountUsernameDisplay = "—";
        HasMinecraftLicense = false;
        MinecraftNickDisplay = "ID: —";
        CurrentAvatarImage = null;
        RefreshSavedAccounts();
        AccountChanged?.Invoke(null, false);
    }

    public byte[]? CurrentSkinRawBytes { get; set; }
    public byte[]? CurrentCapeRawBytes { get; set; }
    public string CurrentSkinModel { get; set; } = "classic";

    [ObservableProperty]
    private LauncherSettings _settings = new();

    private System.Threading.Timer? _settingsDebounceTimer;

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _settingsDebounceTimer?.Dispose();
        _settingsDebounceTimer = new System.Threading.Timer(_ =>
        {
            SaveSettings();
        }, null, 500, System.Threading.Timeout.Infinite);
    }

    partial void OnSettingsChanged(LauncherSettings? oldValue, LauncherSettings newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnSettingsPropertyChanged;
        if (newValue != null) newValue.PropertyChanged += OnSettingsPropertyChanged;
    }

    public void LoadSettings()
    {
        Settings = SettingsService.Load();
    }

    public void SaveSettings()
    {
        SettingsService.Save(Settings);
    }

    public VersionManifest? Manifest { get; set; }

    public List<GameInstance> Instances { get; set; } = new();

    [ObservableProperty]
    private GameInstance? _selectedInstance;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private bool _progressVisible;

    [ObservableProperty]
    private bool _progressIndeterminate;

    [ObservableProperty]
    private string _progressStage = "";

    [ObservableProperty]
    private string _progressPercent = "";

    [ObservableProperty]
    private double _progressValue;

    public void ReportProgress(DownloadProgress p)
    {
        ProgressVisible = true;
        ProgressIndeterminate = false;
        ProgressValue = p.Percent;
        ProgressPercent = $"{p.Percent:F1}%";

        var detail = p.FilesTotal > 1 ? $"  ({p.FilesDone}/{p.FilesTotal})" : "";
        var size = p.BytesTotal > 0 ? $"  ·  {Human(p.BytesDone)} / {Human(p.BytesTotal)}" : "";
        var file = string.IsNullOrEmpty(p.CurrentFile) ? "" : "  —  " + (p.CurrentFile.Length <= 44 ? p.CurrentFile : "…" + p.CurrentFile[^(44 - 1)..]);

        ProgressStage = p.Stage + detail + size + file;
    }

    public CancellationTokenSource? Cts { get; set; }

    public void SetBusy(bool busy, bool keepProgress = false)
    {
        Busy = busy;
        if (!busy && !keepProgress)
        {
            ProgressIndeterminate = false;
        }
    }

    public void SetStage(string stage)
    {
        ProgressVisible = true;
        ProgressStage = stage;
    }

    public void HideProgress()
    {
        ProgressIndeterminate = false;
        ProgressVisible = false;
    }

    private readonly StringBuilder _logBuffer = new();

    [ObservableProperty]
    private string _logText = "";

    public void AppendLog(string line)
    {
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(line);
            if (_logBuffer.Length > 400_000) _logBuffer.Remove(0, 200_000);
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            string text;
            lock (_logBuffer) text = _logBuffer.ToString();
            LogText = text;
        });
    }

    internal static bool MatchesLogLevel(string line, string level)
    {
        var lower = line.ToLowerInvariant();
        var isError = lower.Contains("[error]") || lower.Contains("error]") ||
                      lower.Contains("exception") || lower.Contains("ошибка") ||
                      lower.Contains("не удалось") || lower.Contains("severe") ||
                      lower.Contains("fatal") || lower.Contains("!!!");
        if (level == "error") return isError;
        return isError || lower.Contains("[warn]") || lower.Contains("warn]") ||
               lower.Contains("внимание") || lower.Contains("предупрежд");
    }


    public static string Human(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = (mod10 == 1 && mod100 != 11) ? one
            : (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) ? few
            : many;
        return $"{n} {word}";
    }

    public static string FormatMinutes(long seconds)
    {
        if (seconds < 60) return $"{seconds} с";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} ч {ts.Minutes} мин" : $"{ts.Minutes} мин";
    }

    public int EffectiveMemory(GameInstance inst) =>
        inst.MaxMemoryMb > 0 ? inst.MaxMemoryMb : Settings.MaxMemoryMb;

    [RelayCommand]
    private void Cancel()
    {
        Cts?.Cancel();
        SetStage("Отмена...");
    }

    [RelayCommand]
    private void Logout()
    {
        if (Account is not null)
            Accounts.Remove(Account.Username, Account.Type);

        Accounts.ClearActiveSession();
        ClearAccount();
        AppendLog("Выполнен выход из аккаунта.");
    }

    [RelayCommand]
    private async Task LoginVecAsync()
    {
        if (Dialogs is null) return;

        SetBusy(true);
        SetStage("Авторизация через VEC ID...");

        try
        {
            var vecAcc = await Dialogs.ShowVecLoginAsync();
            if (vecAcc is null)
            {
                AppendLog("Вход через VEC ID отменён.");
                return;
            }

            Accounts.Save(vecAcc);
            SetAccount(vecAcc, refreshSkin: true);
            Dialogs.ShowToast("VEC ID", $"Добро пожаловать, {vecAcc.Username}!", ToastType.Success);
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка авторизации VEC: " + ex.Message);
            Dialogs.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            SetBusy(false);
            HideProgress();
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LauncherConfig.ProjectWebsiteUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось открыть сайт: " + ex.Message);
        }
    }

    public void DoClearLog()
    {
        lock (_logBuffer) _logBuffer.Clear();
        LogText = "";
    }

    public Func<GameInstance, string?, Task>? LaunchAsyncCallback { get; set; }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (Busy)
        {
            Cts?.Cancel();
            SetStage("Отмена...");
            return;
        }

        var inst = SelectedInstance ?? Instances.FirstOrDefault();
        if (inst is null)
        {
            Dialogs?.ShowToast("Нет сборки", "Сначала выберите или создайте сборку Minecraft.", ToastType.Warning);
            return;
        }

        if (Account is null)
        {
            Dialogs?.ShowToast("Нет аккаунта", "Сначала войдите в аккаунт.", ToastType.Warning);
            return;
        }

        var targetIp = string.IsNullOrWhiteSpace(inst.ServerAddress)
            ? LauncherConfig.DefaultMinecraftServer
            : inst.ServerAddress;

        if (LaunchAsyncCallback is not null)
        {
            await LaunchAsyncCallback(inst, targetIp);
            return;
        }

        await ExecuteLaunchAsync(inst, targetIp);
    }

    public async Task ExecuteLaunchAsync(GameInstance inst, string? serverAddress)
    {
        if (Busy) return;

        if (Account is null)
        {
            Dialogs?.ShowMessage(
                "Сначала войдите в аккаунт на вкладке «Аккаунт».\n\n" +
                "Требуется авторизация через VEC ID.", "Требуется вход");
            Navigate("3");
            return;
        }

        Sessions.Prune();
        var runningSessions = Sessions.Sessions.Where(s => s.IsRunning).ToList();
        if (runningSessions.Count > 0)
        {
            var names = string.Join(", ", runningSessions.Select(s => s.InstanceName));
            var isThisRunning = Sessions.IsInstanceRunning(inst.Id);
            var prompt = isThisRunning
                ? $"Сборка «{inst.Name}» уже запущена.\n\nЗапустить ещё одну копию игры?"
                : $"Minecraft уже запущен ({names}).\n\nЗапустить ещё одну копию («{inst.Name}»)?";

            var confirmed = Dialogs != null
                ? await Dialogs.ConfirmAsync("Minecraft уже запущен", prompt, "Запустить", "Отмена")
                : true;

            if (!confirmed)
            {
                return;
            }
        }

        SaveSettings();

        Cts = new CancellationTokenSource();
        var ct = Cts.Token;
        SetBusy(true);

        try
        {
            var result = await Launch.LaunchAsync(
                Account, inst, Settings, Manifest, Instances,
                serverAddress, Settings.AllowMultipleInstances,
                runningSessions.Count > 0, Sessions.IsInstanceRunning(inst.Id),
                Sessions, ct);

            Sessions.Register(new GameSession
            {
                Process = result.Process,
                Pid = result.Process.Id,
                VersionId = result.LaunchId,
                InstanceId = result.InstanceId,
                InstanceName = result.InstanceName
            });

            try { Stats.RecordLaunch(inst.Id, inst.Name); } catch (Exception ex) { Log.Warn(ex.Message); }

            AppendLog($"Minecraft запущен (PID {result.Process.Id}), сборка «{result.InstanceName}».");
            if (serverAddress is not null) AppendLog($"Автоподключение к серверу {serverAddress}.");
            SetStage("Игра запущена");

            if (Settings.CloseLauncherOnStart)
            {
                await Task.Delay(2500, ct);
                Application.Current?.Shutdown();
                return;
            }

            if (Settings.MinimizeOnLaunch) Dialogs?.MinimizeWindow();

            var exitedFast = await Task.Run(() => result.Process.WaitForExit(9000), ct);
            if (exitedFast && result.Process.ExitCode != 0)
            {
                AppendLog($"Игра завершилась сразу с кодом {result.Process.ExitCode}.");
                Dialogs?.ShowMessage(
                    $"Minecraft завершился с кодом {result.Process.ExitCode}.\nОткройте «Консоль» для деталей.", "Игра не запустилась", MessageSeverity.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Операция отменена.");
            SetStage("Отменено");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка запуска", ex);
            Dialogs?.ShowMessage(ex.Message, "Ошибка запуска", MessageSeverity.Error);
            SetStage("Ошибка");
        }
        finally
        {
            SetBusy(false);
            Cts?.Dispose();
            Cts = null;
        }
    }

    public void HandleSessionExited(GameSession session, int code)
    {
        var seconds = (long)session.Uptime.TotalSeconds;

        try
        {
            Stats.RecordPlayTime(session.InstanceId, (int)seconds);
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        var inst = Instances.FirstOrDefault(i => i.Id == session.InstanceId);
        if (inst is not null)
        {
            inst.AddSession(seconds);
            InstanceService?.SaveAll(Instances);
        }

        TotalPlayTimeDisplay = Stats.GetFormattedTotalTime();

        AppendLog($"--- «{session.InstanceName}» завершилась (код {code}), " +
                  $"время сессии {session.UptimeDisplay} ---");
    }

    public void RefreshSavedAccounts()
    {
        SavedAccounts.Clear();
        SavedAccountItems.Clear();

        var all = Accounts.GetAllSaved();
        HasSavedAccounts = all.Count > 0;

        foreach (var acc in all)
        {
            SavedAccounts.Add(acc);

            var isCurrent = Account != null &&
                            Account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) &&
                            Account.Type == acc.Type;

            ImageSource? avatar = null;
            var localFile = SkinService.FindAccountSkin(acc.Username);
            if (localFile != null && File.Exists(localFile))
            {
                try
                {
                    var bytes = File.ReadAllBytes(localFile);
                    avatar = SkinService.CreateCrispHeadAvatar(bytes, 32);
                }
                catch { }
            }

            SavedAccountItems.Add(new AccountItemViewModel(acc, isCurrent, avatar));
        }
    }

    [RelayCommand]
    private void ToggleSavedAccountsPopup()
    {
        if (!IsSavedAccountsPopupOpen)
        {
            RefreshSavedAccounts();
        }
        IsSavedAccountsPopupOpen = !IsSavedAccountsPopupOpen;
    }

    [RelayCommand]
    private void SelectSavedAccount(AccountItemViewModel? item)
    {
        if (item is null) return;
        IsSavedAccountsPopupOpen = false;
        Accounts.Save(item.Account);
        SetAccount(item.Account, refreshSkin: true);
        Dialogs?.ShowToast("Профиль переключен", $"Активен аккаунт: {item.Username}", ToastType.Info);
    }

    [RelayCommand]
    private async Task SidebarAddAccountAsync()
    {
        IsSavedAccountsPopupOpen = false;
        await LoginVecCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void SidebarLogout()
    {
        IsSavedAccountsPopupOpen = false;
        Logout();
    }

    [RelayCommand]
    private void SelectAccount(MinecraftAccount acc)
    {
        Accounts.Save(acc);
        SetAccount(acc, refreshSkin: true);
        RefreshSavedAccounts();
    }

    [RelayCommand]
    private void RemoveAccount(MinecraftAccount acc)
    {
        Accounts.Remove(acc.Username, acc.Type);
        if (Account?.Username == acc.Username && Account?.Type == acc.Type)
        {
            Accounts.ClearActiveSession();
            ClearAccount();
        }
        RefreshSavedAccounts();
    }

    public async Task RefreshServerPingAsync()
    {
        try
        {
            var status = await Ping.PingAsync(LauncherConfig.DefaultMinecraftServer);
            ServerPlayersDisplay = status.Online ? $"{status.OnlinePlayers} / {status.MaxPlayers}" : "офлайн";
        }
        catch
        {
            ServerPlayersDisplay = "офлайн";
        }
    }
}
