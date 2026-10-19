using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _homePlayButtonText = "ИГРАТЬ";

    [ObservableProperty]
    private string _homePlayButtonBackground = "#F59E0B";

    [ObservableProperty]
    private string _homePlayButtonBorderBrush = "#FBBF24";

    [ObservableProperty]
    private string _homePlayButtonForeground = "#08090D";

    partial void OnBusyChanged(bool value)
    {
        UpdateHomePlayButtonState();
    }

    public void UpdateHomePlayButtonState()
    {
        if (Busy)
        {
            HomePlayButtonText = "ОТМЕНА";
            HomePlayButtonBackground = "#333333";
            HomePlayButtonBorderBrush = "#444444";
            HomePlayButtonForeground = "#FFFFFF";
        }
        else
        {
            HomePlayButtonText = "ИГРАТЬ";
            HomePlayButtonBackground = "#F59E0B";
            HomePlayButtonBorderBrush = "#FBBF24";
            HomePlayButtonForeground = "#08090D";
        }
    }

    [RelayCommand]
    public async Task LoadVersionsAsync()
    {
        try
        {
            if (Manifest is null)
            {
                SetStage("Загружаю манифест версий Mojang...");
                ProgressIndeterminate = true;
                ProgressVisible = true;
                Manifest = await Versions.GetManifestAsync();
            }

            var supported = VersionService.FilterSupported(Manifest, Settings.ShowSnapshots);
            AppendLog($"Манифест загружен: {Manifest.Versions.Count} версий, доступно {supported.Count} (≥1.16.5).");
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка загрузки версий: " + ex.Message);
        }
        finally
        {
            HideProgress();
        }
    }

    [RelayCommand]
    public void LoadInstances()
    {
        if (InstanceService is null) return;

        if (Instances is null || Instances.Count == 0)
        {
            Instances = InstanceService.LoadAll();
        }

        if (!InstanceService.Loaded)
        {
            AppendLog("ВНИМАНИЕ: список сборок не прочитан, изменения не сохраняются. Перезапустите лаунчер.");
            Dialogs?.ShowMessage(
                "Не удалось прочитать список сборок.\n\n" +
                "Чтобы не потерять данные, сохранение отключено до перезапуска.\n" +
                "Файлы сборок на диске не тронуты.",
                "Список сборок", MessageSeverity.Warning);
        }

        var orphans = InstanceService.ScanOrphans(Instances);
        if (orphans.Count > 0)
        {
            Instances.AddRange(orphans);
            InstanceService.SaveAll(Instances);
            AppendLog($"Найдено сборок на диске: {orphans.Count}.");
        }

        if (InstanceService.Loaded && !Instances.Any(i => i.Name.Equals("VEC Fabric 1.21.11", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultInst = new GameInstance
            {
                Name = "VEC Fabric 1.21.11",
                McVersion = "1.21.11",
                Loader = LoaderKind.Fabric,
                ServerAddress = LauncherConfig.DefaultMinecraftServer
            };
            InstanceService.EnsureFolders(defaultInst);
            Instances.Insert(0, defaultInst);
            InstanceService.SaveAll(Instances);
            AppendLog($"Создана официальная сборка сервера «{defaultInst.Name}».");
        }

        RefreshInstanceLists();
        VerifyInstalledVersions();
    }

    public void VerifyInstalledVersions()
    {
        if (Instances is null) return;
        var missing = new List<string>();

        foreach (var inst in Instances)
        {
            try
            {
                var paths = GamePaths.ForInstance(inst);
                if (!File.Exists(paths.VersionJar(inst.McVersion)))
                    missing.Add($"{inst.Name} ({inst.McVersion})");
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        if (missing.Count > 0)
            AppendLog($"Требуют загрузки клиента: {string.Join(", ", missing)}. Файлы скачаются при нажатии «ИГРАТЬ».");
    }

    public void RefreshInstanceLists()
    {
        if (Instances is null) return;
        var ordered = Instances.OrderByDescending(i => i.LastPlayed ?? i.Created).ToList();

        var target = ordered.FirstOrDefault(i => i.Id == Settings.LastInstanceId)
            ?? ordered.FirstOrDefault(i => i.Name.Equals("VEC Fabric 1.21.11", StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault();
        if (target is not null)
        {
            SelectInstance(target);
        }
        else
        {
            SelectedInstance = null;
        }
    }

    public void SelectInstance(GameInstance inst)
    {
        SelectedInstance = inst;
        Settings.LastInstanceId = inst.Id;
        UpdateHomePlayButtonState();
    }
}
