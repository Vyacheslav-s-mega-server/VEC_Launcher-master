using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public sealed partial class MaintenanceItemViewModel : ObservableObject
{
    public MaintenanceService.CleanTarget Target { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string SizeDisplay { get; init; } = "";
    public bool Dangerous { get; init; }

    public System.Windows.Media.Brush TitleColor => Dangerous
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71))
        : System.Windows.Media.Brushes.White;

    public System.Windows.Media.Brush RowBorder => Dangerous
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x24, 0x28))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A));

    [ObservableProperty]
    private bool _isChecked;
}

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private ObservableCollection<JavaInstallation> _detectedJavaList = new();

    [ObservableProperty]
    private string _javaScanStatus = "";

    [ObservableProperty]
    private string _maintenanceTotalDisplay = "Нажмите «Пересчитать»";

    [ObservableProperty]
    private string _maintenanceMessage = "";

    [ObservableProperty]
    private ObservableCollection<MaintenanceItemViewModel> _maintenanceItems = new();

    public string MemoryHintDisplay
    {
        get
        {
            var totalRam = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
            return totalRam > 0
                ? $"Всего в системе: {totalRam} МБ. Для ванильной игры обычно достаточно 2048–4096 МБ."
                : "Для ванильной игры обычно достаточно 2048–4096 МБ.";
        }
    }

    public bool IsGameLangRu
    {
        get => Settings.GameLanguage != "en";
        set
        {
            if (value)
            {
                Settings.GameLanguage = "ru";
                SaveSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGameLangEn));
            }
        }
    }

    public bool IsGameLangEn
    {
        get => Settings.GameLanguage == "en";
        set
        {
            if (value)
            {
                Settings.GameLanguage = "en";
                SaveSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGameLangRu));
            }
        }
    }

    [RelayCommand]
    private void SetMemoryMb(object? param)
    {
        int mb = param switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 4096
        };
        Settings.MaxMemoryMb = Math.Clamp(mb, 1024, 16384);
        SaveSettings();
    }

    [RelayCommand]
    private void SetMemoryRecommended()
    {
        Settings.MaxMemoryMb = LauncherSettings.RecommendedMaxMemory();
        SaveSettings();
    }

    [RelayCommand]
    private void SetResolution(string res)
    {
        var parts = res.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
        {
            Settings.WindowWidth = w;
            Settings.WindowHeight = h;
            Settings.Fullscreen = false;
            SaveSettings();
        }
    }

    [RelayCommand]
    private void SetLanguage(string lang)
    {
        Settings.GameLanguage = lang;
        SaveSettings();
        OnPropertyChanged(nameof(IsGameLangRu));
        OnPropertyChanged(nameof(IsGameLangEn));
    }

    [RelayCommand]
    private void BrowseJava()
    {
        if (Dialogs is null) return;
        var file = Dialogs.BrowseFile("java.exe|java.exe;javaw.exe|Исполняемые файлы (*.exe)|*.exe", "Выберите java.exe");
        if (string.IsNullOrWhiteSpace(file)) return;

        var probe = JavaService.Probe(file, "custom");
        if (probe is null)
        {
            Dialogs.ShowMessage("Не удалось определить версию Java.", "Java", MessageSeverity.Warning);
            return;
        }

        Settings.CustomJavaPath = file;
        SaveSettings();
        AppendLog("Выбрана Java: " + probe);
    }

    [RelayCommand]
    private void ClearJava()
    {
        Settings.CustomJavaPath = "";
        SaveSettings();
        AppendLog("Сброшен путь к Java (автоматический выбор).");
    }

    [RelayCommand]
    private void SelectJava(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Settings.CustomJavaPath = path;
        SaveSettings();
        AppendLog($"Выбрана установленная Java: {path}");
    }

    [RelayCommand]
    private void BrowseGameDir()
    {
        if (Dialogs is null) return;
        var folder = Dialogs.BrowseFolder("Выберите папку для данных лаунчера");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Settings.GameDir = folder;
            SaveSettings();
            AppendLog("Изменена папка лаунчера: " + folder);
        }
    }

    [RelayCommand]
    private void OpenGameDir()
    {
        var dir = string.IsNullOrWhiteSpace(Settings.GameDir) ? LauncherPaths.Root : Settings.GameDir;
        try
        {
            if (InstanceService != null) InstanceService.OpenFolder(dir);
            else Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось открыть папку: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        JavaScanStatus = "Поиск установленных версий Java…";
        DetectedJavaList.Clear();

        try
        {
            var list = await Task.Run(() => Java.FindAll());
            foreach (var j in list)
            {
                DetectedJavaList.Add(j);
            }

            JavaScanStatus = DetectedJavaList.Count == 0
                ? "Java не обнаружена. Лаунчер скачает нужную версию автоматически."
                : "";
        }
        catch (Exception ex)
        {
            Log.Warn("Ошибка поиска Java: " + ex.Message);
            JavaScanStatus = "Ошибка поиска Java";
        }
    }

    [RelayCommand]
    private void ScanMaintenance()
    {
        if (Maintenance is null) return;
        MaintenanceTotalDisplay = "Считаю…";

        var targets = Maintenance.Enumerate();
        var total = Maintenance.TotalSize();
        MaintenanceTotalDisplay = $"Всего данных лаунчера: {Human(total)}  ·  {LauncherPaths.Root}";

        MaintenanceItems.Clear();
        foreach (var t in targets)
        {
            MaintenanceItems.Add(new MaintenanceItemViewModel
            {
                Target = t.Target,
                Title = t.Title,
                Description = t.Description,
                SizeDisplay = t.SizeDisplay,
                Dangerous = t.Dangerous,
                IsChecked = false
            });
        }
    }

    [RelayCommand]
    private void SelectSafeMaintenance()
    {
        foreach (var item in MaintenanceItems)
        {
            item.IsChecked = !item.Dangerous && item.Target is MaintenanceService.CleanTarget.Cache
                or MaintenanceService.CleanTarget.ImageCache
                or MaintenanceService.CleanTarget.Logs;
        }
    }

    [RelayCommand]
    private async Task CleanMaintenanceAsync()
    {
        if (Maintenance is null || Dialogs is null) return;

        var selected = MaintenanceItems.Where(i => i.IsChecked).ToList();
        if (selected.Count == 0)
        {
            Dialogs.ShowMessage("Отметьте, что нужно удалить.", "Ничего не выбрано");
            return;
        }

        if (Sessions.AnyRunning)
        {
            Dialogs.ShowMessage("Сначала остановите игру.", "Игра запущена", MessageSeverity.Warning);
            return;
        }

        var targets = Maintenance.Enumerate().Where(t => selected.Any(s => s.Target == t.Target)).ToList();
        var totalSize = targets.Sum(t => t.Size);
        var msg = "Будет удалено:\n\n" +
                  string.Join("\n", targets.Select(t => $"  • {t.Title} — {t.SizeDisplay}")) +
                  $"\n\nОсвободится примерно {Human(totalSize)}.";

        if (selected.Any(s => s.Dangerous))
        {
            msg += "\n\nВНИМАНИЕ: среди выбранного есть сборки с модами и мирами. Восстановить их будет невозможно.";
        }

        if (!await Dialogs.ConfirmAsync("Подтверждение очистки", msg + "\n\nПродолжить?")) return;

        var freed = Maintenance.Clean(targets);
        if (selected.Any(s => s.Target == MaintenanceService.CleanTarget.ImageCache)) ImageCache?.ClearMemory();

        ScanMaintenance();
        Dialogs.ShowMessage($"Готово. Освобождено {Human(freed)}.", "Очистка завершена");
        AppendLog($"Очистка: освобождено {Human(freed)}");
    }

    [RelayCommand]
    private void ClearCache()
    {
        try
        {
            var freed = 0L;
            if (Directory.Exists(LauncherPaths.CacheDir))
            {
                foreach (var f in Directory.GetFiles(LauncherPaths.CacheDir))
                {
                    if (f.EndsWith("version_manifest_v2.json", StringComparison.OrdinalIgnoreCase)) continue;
                    try { freed += new FileInfo(f).Length; File.Delete(f); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }

            MaintenanceMessage = $"Кэш очищен, освобождено {Human(freed)}.";
            AppendLog($"Кэш очищен ({Human(freed)}).");
        }
        catch (Exception ex)
        {
            MaintenanceMessage = "Не удалось очистить кэш: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        if (Dialogs is null) return;
        var r = await Dialogs.ConfirmAsync(
            "Сброс настроек",
            "Сбросить все настройки лаунчера к значениям по умолчанию?\n\n" +
            "Сборки, моды и аккаунт затронуты не будут.");

        if (!r) return;

        Settings = new LauncherSettings
        {
            MaxMemoryMb = LauncherSettings.RecommendedMaxMemory(),
            GameDir = LauncherPaths.Root
        };

        SaveSettings();
        ThemeService.ApplyAccent(Settings.AccentColor);

        MaintenanceMessage = "Настройки сброшены.";
        AppendLog("Настройки сброшены к значениям по умолчанию.");
    }

    [RelayCommand]
    private async Task ReinstallSoftAsync()
    {
        if (Maintenance is null || Dialogs is null) return;

        if (!await Dialogs.ConfirmAsync("Переустановка начисто",
                "Будут удалены версии игры, библиотеки, ресурсы, Java и кэш.\n\n" +
                "Сборки (моды, миры, скриншоты), аккаунт и настройки сохранятся.\n" +
                "Файлы игры скачаются заново при следующем запуске.\n\nПродолжить?")) return;

        if (Sessions.AnyRunning)
        {
            Dialogs.ShowMessage("Сначала остановите игру.", "Игра запущена", MessageSeverity.Warning);
            return;
        }

        var targets = Maintenance.Enumerate()
            .Where(t => t.Target is MaintenanceService.CleanTarget.Versions
                or MaintenanceService.CleanTarget.Libraries
                or MaintenanceService.CleanTarget.Assets
                or MaintenanceService.CleanTarget.JavaRuntime
                or MaintenanceService.CleanTarget.Cache
                or MaintenanceService.CleanTarget.ImageCache)
            .ToList();

        var freed = Maintenance.Clean(targets);
        ImageCache?.ClearMemory();
        ScanMaintenance();

        Dialogs.ShowMessage($"Готово. Освобождено {Human(freed)}.\n\n" +
                        "Файлы игры загрузятся заново при нажатии «ИГРАТЬ».", "Переустановка");
    }

    [RelayCommand]
    private async Task ReinstallFullAsync()
    {
        if (Maintenance is null || Dialogs is null) return;

        if (!await Dialogs.ConfirmAsync("Полная переустановка",
                "Будут удалены ВСЕ данные лаунчера:\n\n" +
                "  • версии игры, библиотеки, ресурсы\n" +
                "  • сборки со всеми модами и мирами\n" +
                "  • аккаунт и настройки\n\n" +
                "Сам файл лаунчера останется. Восстановить данные будет нельзя.\n\nПродолжить?")) return;

        if (!await Dialogs.ConfirmAsync("Последнее подтверждение", "Точно удалить все миры и моды?")) return;

        if (Sessions.AnyRunning) await Sessions.StopAllAsync();

        var freed = Maintenance.Clean(Maintenance.Enumerate());

        Dialogs.ShowMessage($"Удалено {Human(freed)}.\n\nЛаунчер сейчас закроется. " +
                        "Запустите его заново — он будет как после установки.", "Готово");

        System.Windows.Application.Current?.Shutdown();
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (Maintenance is null || Dialogs is null) return;

        var exePath = Environment.ProcessPath ?? "";
        var isExe = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var r = await Dialogs.ConfirmCancelAsync("Удаление лаунчера",
            "Полностью удалить VEC Launcher с компьютера?\n\n" +
            $"Будет удалена папка данных:\n{LauncherPaths.Root}\n\n" +
            (isExe ? "«Да» — удалить и сам файл лаунчера.\n«Нет» — удалить только данные.\n"
                   : "Файл лаунчера удалить нельзя (запущен не как exe).\n") +
            "\nЭто действие необратимо.",
            "Да", "Только данные", "Отмена");

        if (r == ConfirmResult.Cancel) return;

        var removeExe = isExe && r == ConfirmResult.Yes;

        if (!await Dialogs.ConfirmAsync("Последнее подтверждение",
                removeExe
                    ? "Лаунчер удалит все данные и себя, затем закроется. Подтвердить?"
                    : "Лаунчер удалит все данные и закроется. Подтвердить?")) return;

        try
        {
            if (Sessions.AnyRunning) await Sessions.StopAllAsync();

            var script = Maintenance.PrepareUninstall(removeExe);
            Maintenance.RunUninstall(script);

            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            Dialogs.ShowMessage("Не удалось запустить удаление: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }
}
