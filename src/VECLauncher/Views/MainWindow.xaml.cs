using IOPath = System.IO.Path;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel Vm { get; }

    private VersionService _versions;
    private DownloadManager _downloads;
    private JavaService _java;
    private SkinService _skins;
    private GameLauncher _game;
    private ModLoaderService _loaders;
    private ServerPingService _ping;
    private GameSessionManager _sessions => Vm.Sessions;
    private GameStatistics _stats => Vm.Stats;
    private readonly IDialogService _dialog;
    private readonly IInstanceService _instancesService;
    private readonly IMaintenanceService _maintenance;
    private readonly IImageCacheService _imageCache;
    private readonly object _skinFileLock = new();
    private PreloadResult? _preloadedData;

    public void ApplyPreloadResult(PreloadResult result)
    {
        _preloadedData = result;
        if (result.ServerManifest.Count > 0)
        {
            SetPreloadedManifest(result.ServerManifest);
        }
        if (result.VersionManifest != null)
        {
            _manifest = result.VersionManifest;
        }
        if (result.Instances.Count > 0)
        {
            _instances = result.Instances;
        }
    }

    private LauncherSettings _settings { get => Vm.Settings; set => Vm.Settings = value; }
    private MinecraftAccount? _account { get => Vm.Account; set => Vm.Account = value; }
    private VersionManifest? _manifest { get => Vm.Manifest; set => Vm.Manifest = value; }
    private List<GameInstance> _instances { get => Vm.Instances; set => Vm.Instances = value; }
    private GameInstance? _selectedInstance { get => Vm.SelectedInstance; set => Vm.SelectedInstance = value; }
    private CancellationTokenSource? _cts { get => Vm.Cts; set => Vm.Cts = value; }
    private bool _busy { get => Vm.Busy; set => Vm.Busy = value; }
    private bool _initializing = true;
    private DateTime _lastProgressUi = DateTime.MinValue;

    public MainWindow(MainWindowViewModel vm,
        IInstanceService instancesService,
        IMaintenanceService maintenance, IImageCacheService imageCache)
    {
        Vm = vm;
        _instancesService = instancesService;
        _maintenance = maintenance;
        _imageCache = imageCache;
        DataContext = Vm;

        InitializeComponent();

        _dialog = Vm.Dialogs;

        _versions = Vm.Versions;
        _downloads = Vm.Downloads;
        _java = Vm.Java;
        _skins = Vm.Skins;
        _game = Vm.Game;
        _loaders = Vm.Loaders;
        _ping = Vm.Ping;

        ToastNotification.Initialize(this);

        _downloads.Progress += OnProgress;
        _java.Progress += OnProgress;
        _loaders.Status += s => Dispatcher.Invoke(() => SetStage(s));
        _game.GameOutput += line => Vm.AppendLog(line);
        Log.LineWritten += line => Vm.AppendLog(line);

        Vm.Sessions.Changed += () => Dispatcher.BeginInvoke(UpdateRunStateUi);
        _sessions.SessionExited += OnSessionExited;
        Vm.AccountChanged += OnAccountChanged;
        Vm.CapeTextureChanged += OnCapeTextureChanged;
        Vm.SkinTextureChanged += OnSkinTextureChanged;
        Vm.SkinModelChanged += OnSkinModelChanged;

        Loaded += OnLoadedAsync;
        Closing += OnClosing;
        KeyDown += Window_KeyDown;
        Activated += MainWindow_Activated;
        InitSkinDirectoryWatchers();
        StartContentPolling();
    }

    private FileSystemWatcher? _skinWatcher;
    private FileSystemWatcher? _capeWatcher;
    private DateTime _lastWatcherReload = DateTime.MinValue;
    private bool _isInternalSkinWrite = false;

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (_account != null && (DateTime.UtcNow - _lastWatcherReload).TotalSeconds > 2)
        {
            _lastWatcherReload = DateTime.UtcNow;
            _ = LoadSkinImagesAsync(_account);
            if (PageAccount?.Visibility == Visibility.Visible)
            {
                _ = RefreshCapesGridUIAsync(_account);
            }
        }
        if (PageContent?.Visibility == Visibility.Visible && _selectedInstance != null)
        {
            RefreshContent(false);
        }
    }

    private void InitSkinDirectoryWatchers()
    {
        try
        {
            var skinsDir = System.IO.Path.Combine(LauncherPaths.Root, "skins");
            Directory.CreateDirectory(skinsDir);
            _skinWatcher = new FileSystemWatcher(skinsDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _skinWatcher.Changed += OnSkinFileChanged;
            _skinWatcher.Created += OnSkinFileChanged;

            var capesDir = System.IO.Path.Combine(LauncherPaths.Root, "capes");
            Directory.CreateDirectory(capesDir);
            _capeWatcher = new FileSystemWatcher(capesDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _capeWatcher.Changed += OnSkinFileChanged;
            _capeWatcher.Created += OnSkinFileChanged;
        }
        catch (Exception ex)
        {
            Log.Warn("Skin directory watcher failed: " + ex.Message);
        }
    }

    private void OnSkinFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_account == null || _isInternalSkinWrite || MainWindowViewModel.IsInternalSkinWrite) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(e.Name);
        if (string.Equals(name, _account.Username, StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if ((DateTime.UtcNow - _lastWatcherReload).TotalSeconds < 3) return;
                _lastWatcherReload = DateTime.UtcNow;
                await Task.Delay(500);
                if (_isInternalSkinWrite || MainWindowViewModel.IsInternalSkinWrite) return;
                await LoadSkinImagesAsync(_account);
                await RefreshCapesGridUIAsync(_account);
            });
        }
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _initializing = true;
            _settings = SettingsService.Load();

            ThemeService.ApplyAccent(_settings.AccentColor);
            ApplyWindowBackground();

            AppendLog("VEC Launcher запущен. Папка: " + _settings.GameDir);

            if (_preloadedData?.JavaInstallations != null && _preloadedData.JavaInstallations.Count > 0)
            {
                Vm.DetectedJavaList.Clear();
                foreach (var j in _preloadedData.JavaInstallations) Vm.DetectedJavaList.Add(j);
                Vm.JavaScanStatus = "";
            }
            else
            {
                _ = Vm.DetectJavaCommand.ExecuteAsync(null);
            }

            var saved = _preloadedData?.ActiveAccount ?? Vm.Accounts.GetActive();
            if (saved is not null)
            {
                SetAccount(saved, refreshSkin: true);
            }

            await LoadVersionsAsync();
            LoadInstances();
            _initializing = false;
            UpdateRunStateUi();

            _ = Dispatcher.BeginInvoke(new Action(() => SetupWheelHandling(this)),
                System.Windows.Threading.DispatcherPriority.Loaded);

            UpdateStatisticsDisplay();
            StartServerPingTimer();

            InitNative3DSkinViewer();
            if (_account != null) _ = LoadSkinImagesAsync(_account);
        }
        catch (Exception ex)
        {
            Log.Error("MainWindow OnLoadedAsync error: " + ex);
        }
    }

    private string _currentSkinModel = "classic";
    private byte[]? _currentSkinRawBytes = null;
    private byte[]? _currentCapeRawBytes = null;
    private bool _autoRotateEnabled = false;
    private double _rotAngleY = 205;
    private double _rotAngleX = 10;
    private Point _lastMousePos;
    private bool _isDraggingSkin = false;
    private DispatcherTimer? _skinRotateTimer;    private readonly RotateTransform3D _skinRotY = new(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 205));
    private readonly RotateTransform3D _skinRotX = new(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 10));


    private ModelVisual3D? _groundVisual;

    private void InitNative3DSkinViewer()
    {
        RenderOptions.SetBitmapScalingMode(SkinViewport3D, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(SkinViewport3D, EdgeMode.Aliased);
        if (SkinViewerHost != null)
        {
            RenderOptions.SetBitmapScalingMode(SkinViewerHost, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(SkinViewerHost, EdgeMode.Aliased);
        }

        var group = new Transform3DGroup();
        group.Children.Add(_skinRotY);
        group.Children.Add(_skinRotX);
        SkinModelVisual.Transform = group;

        BuildGroundAndShadow();

        _skinRotateTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _skinRotateTimer.Tick += (s, e) =>
        {
            if (_autoRotateEnabled && !_isDraggingSkin && PageAccount.Visibility == Visibility.Visible)
            {
                _rotAngleY = (_rotAngleY + 0.6) % 360;
                _skinRotY.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _rotAngleY);
            }
        };
        _skinRotateTimer.Start();
    }

    private System.Windows.Point _mouseDownPos;

    private void SkinViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingSkin = true;
            _lastMousePos = e.GetPosition(SkinViewerHost);
            _mouseDownPos = _lastMousePos;
            SkinViewerHost.CaptureMouse();
        }
    }

    private void UpdateStatisticsDisplay()
    {
        Vm.TotalPlayTimeDisplay = _stats.GetFormattedTotalTime();
    }

    private DispatcherTimer? _serverPingTimer;

    private async void PingServerOnce()
    {
        try
        {
            var status = await _ping.PingAsync(LauncherConfig.DefaultMinecraftServer);
            Vm.ServerPlayersDisplay = status.Online
                ? $"{status.OnlinePlayers} / {status.MaxPlayers}"
                : "офлайн";
        }
        catch
        {
            Vm.ServerPlayersDisplay = "офлайн";
        }
    }

    private void StartServerPingTimer()
    {
        PingServerOnce();
        _serverPingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _serverPingTimer.Tick += (_, _) => PingServerOnce();
        _serverPingTimer.Start();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_sessions.AnyRunning)
        {
            var r = await _dialog.ConfirmCancelAsync(
                "Игра запущена",
                $"Сейчас запущено игр: {_sessions.RunningCount}.\n\n" +
                "Закрыть лаунчер вместе с игрой?\n" +
                "«Нет» — лаунчер закроется, игра продолжит работать.",
                "Да с игрой", "Только лаунчер", "Отмена");

            if (r == ConfirmResult.Cancel) { e.Cancel = true; return; }
            if (r == ConfirmResult.Yes) await _sessions.StopAllAsync();
        }

        PersistSettings();
        Application.Current.Shutdown();
    }

    private string _currentSettingsSection = "game";

    private void SettingsSection_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || SecPanelGame is null) return;

        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "game";
        _currentSettingsSection = tag;
        SecPanelGame.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelJava.Visibility = tag == "java" ? Visibility.Visible : Visibility.Collapsed;
        SecPanelStorage.Visibility = tag == "storage" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "storage")
        {
            Vm.ScanMaintenanceCommand.Execute(null);
        }
        else if (tag == "java")
        {
            if (Vm.DetectedJavaList.Count == 0)
            {
                _ = Vm.DetectJavaCommand.ExecuteAsync(null);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : "…" + s[^(max - 1)..];
}
