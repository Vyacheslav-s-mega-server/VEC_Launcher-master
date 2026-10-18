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
    public readonly ModpackService Modpacks;

    public readonly GameSessionManager Sessions = new();
    public readonly GameStatistics Stats;
    public readonly VecAuthService VecAuth = new();
    public readonly LaunchService Launch;
    public readonly IAccountRepository Accounts;
    public IDialogService Dialogs { get; private set; } = null!;

    public MainWindowViewModel(
        VersionService versions,
        DownloadManager downloads,
        JavaService java,
        SkinService skins,
        GameLauncher game,
        ModLoaderService loaders,
        ServerPingService ping,
        ModpackService modpacks,
        LaunchService launch,
        GameStatistics stats,
        IAccountRepository accounts)
    {
        Versions = versions;
        Downloads = downloads;
        Java = java;
        Skins = skins;
        Game = game;
        Loaders = loaders;
        Ping = ping;
        Modpacks = modpacks;
        Stats = stats;
        Accounts = accounts;
        Launch = launch;
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

    public void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        Account = acc;
        IsAccountLoggedIn = true;

        if (acc.IsVec)
        {
            SideName = acc.Username;
            SideStatus = "VEC Platform";
        }
        else
        {
            SideName = acc.Username;
            SideStatus = "Microsoft";
        }
    }

    public void ClearAccount()
    {
        Account = null;
        SideName = "Не выполнен вход";
        SideStatus = "Оффлайн";
        IsAccountLoggedIn = false;
    }

    public byte[]? CurrentSkinRawBytes { get; set; }
    public byte[]? CurrentCapeRawBytes { get; set; }
    public string CurrentSkinModel { get; set; } = "classic";

    [ObservableProperty]
    private LauncherSettings _settings = new();

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

    public void LoadInstances()
    {
        Instances = InstanceService.LoadAll();
    }

    public void SelectInstance(GameInstance inst)
    {
        SelectedInstance = inst;
        Settings.LastInstanceId = inst.Id;
    }

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
    private string _logFilter = "all";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _logInfo = "Журнал лаунчера и вывод игры";

    public void AppendLog(string line)
    {
        lock (_logBuffer)
        {
            _logBuffer.AppendLine(line);
            if (_logBuffer.Length > 400_000) _logBuffer.Remove(0, 200_000);
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_logFilter != "all")
            {
                if (MatchesLogLevel(line, _logFilter)) ApplyLogFilterInternal();
                return;
            }

            string text;
            lock (_logBuffer) text = _logBuffer.ToString();
            LogText = text;
        });
    }

    public void SetLogFilter(string filter)
    {
        _logFilter = filter;
        ApplyLogFilterInternal();
    }

    private void ApplyLogFilterInternal()
    {
        string all;
        lock (_logBuffer) all = _logBuffer.ToString();

        if (_logFilter == "all")
        {
            LogText = all;
            LogInfo = "Журнал лаунчера и вывод игры";
            return;
        }

        var lines = all.Split('\n');
        var filtered = lines.Where(l => MatchesLogLevel(l, _logFilter)).ToList();

        LogText = filtered.Count > 0
            ? string.Join("\n", filtered)
            : (_logFilter == "error" ? "Ошибок нет." : "Предупреждений нет.");

        LogInfo = $"Показано {filtered.Count} из {lines.Length} строк";
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
        }
    }
}
