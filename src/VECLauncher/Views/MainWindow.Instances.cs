using IOPath = System.IO.Path;
using System.IO;
using System.Windows.Controls;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private async Task LoadVersionsAsync()
    {
        try
        {
            if (_manifest is null)
            {
                SetStage("Загружаю манифест версий Mojang...");
                ShowProgress(indeterminate: true);
                _manifest = await _versions.GetManifestAsync();
            }

            var supported = VersionService.FilterSupported(_manifest, _settings.ShowSnapshots);
            AppendLog($"Манифест загружен: {_manifest.Versions.Count} версий, доступно {supported.Count} (≥1.16.5).");
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка загрузки версий: " + ex.Message);
        }
        finally { HideProgress(); }
    }

    private void LoadInstances()
    {
        if (_instances is null || _instances.Count == 0)
        {
            _instances = _instancesService.LoadAll();
        }

        if (!_instancesService.Loaded)
        {
            AppendLog("ВНИМАНИЕ: список сборок не прочитан, изменения не сохраняются. Перезапустите лаунчер.");
            _dialog.ShowMessage(
                "Не удалось прочитать список сборок.\n\n" +
                "Чтобы не потерять данные, сохранение отключено до перезапуска.\n" +
                "Файлы сборок на диске не тронуты.",
                "Список сборок", MessageSeverity.Warning);
        }

        var orphans = _instancesService.ScanOrphans(_instances);
        if (orphans.Count > 0)
        {
            _instances.AddRange(orphans);
            _instancesService.SaveAll(_instances);
            AppendLog($"Найдено сборок на диске: {orphans.Count}.");
        }

        if (_instancesService.Loaded && !_instances.Any(i => i.Name.Equals("VEC Fabric 1.21.11", StringComparison.OrdinalIgnoreCase)))
        {
            var defaultInst = new GameInstance
            {
                Name = "VEC Fabric 1.21.11",
                McVersion = "1.21.11",
                Loader = LoaderKind.Fabric,
                ServerAddress = LauncherConfig.DefaultMinecraftServer
            };
            _instancesService.EnsureFolders(defaultInst);
            _instances.Insert(0, defaultInst);
            _instancesService.SaveAll(_instances);
            AppendLog($"Создана официальная сборка сервера «{defaultInst.Name}».");
        }

        RefreshInstanceLists();
        VerifyInstalledVersions();
    }

    private void VerifyInstalledVersions()
    {
        var missing = new List<string>();

        foreach (var inst in _instances)
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

    private void RefreshInstanceLists()
    {
        var ordered = _instances.OrderByDescending(i => i.LastPlayed ?? i.Created).ToList();

        var target = ordered.FirstOrDefault(i => i.Id == _settings.LastInstanceId)
            ?? ordered.FirstOrDefault(i => i.Name.Equals("VEC Fabric 1.21.11", StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault();
        if (target is not null)
        {
            SelectInstance(target);
        }
        else
        {
            _selectedInstance = null;
        }
    }

    private void SelectInstance(GameInstance inst)
    {
        _selectedInstance = inst;
        _settings.LastInstanceId = inst.Id;
        UpdateRunStateUi();
    }


    private enum HomeButtonState { Idle, Busy, Running }
}
