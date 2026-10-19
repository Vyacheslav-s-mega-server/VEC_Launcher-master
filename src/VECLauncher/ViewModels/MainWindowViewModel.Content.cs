using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<ContentTileItemViewModel> RequiredContentItems { get; } = new();
    public ObservableCollection<ContentTileItemViewModel> OptionalContentItems { get; } = new();

    [ObservableProperty]
    private ContentKind _selectedContentKind = ContentKind.Mods;

    [ObservableProperty]
    private string _contentSubtitle = "";

    [ObservableProperty]
    private string _modsBadgeText = "Моды";

    [ObservableProperty]
    private string _resourcePacksBadgeText = "Ресурспаки";

    [ObservableProperty]
    private string _shadersBadgeText = "Шейдеры";

    [ObservableProperty]
    private bool _hasRequiredContent;

    [ObservableProperty]
    private bool _hasOptionalContent;

    [RelayCommand]
    public Task RefreshContentAsync() => RefreshContentCoreAsync(force: true);

    public Task RefreshContentAsync(bool force) => RefreshContentCoreAsync(force);

    public async Task RefreshContentCoreAsync(bool force = false)
    {
        if (Content is null || SelectedInstance is null)
        {
            RequiredContentItems.Clear();
            OptionalContentItems.Clear();
            HasRequiredContent = false;
            HasOptionalContent = false;
            return;
        }

        ContentSubtitle = $"Сборка «{SelectedInstance.Name}» · {SelectedInstance.McVersion} · {SelectedInstance.LoaderDisplay}";

        var (required, optional) = await Content.LoadContentItemsAsync(SelectedInstance, SelectedContentKind, Account, force);

        RequiredContentItems.Clear();
        foreach (var req in required)
            RequiredContentItems.Add(req);

        OptionalContentItems.Clear();
        foreach (var opt in optional)
            OptionalContentItems.Add(opt);

        HasRequiredContent = RequiredContentItems.Count > 0;
        HasOptionalContent = OptionalContentItems.Count > 0;

        var manifest = await Content.FetchBuildManifestAsync(Account, false);
        var (modsCount, rpCount, shadersCount) = Content.GetCategoryCounts(manifest);
        if (manifest.Count > 0)
        {
            ModsBadgeText = $"Моды ({modsCount})";
            ResourcePacksBadgeText = $"Ресурспаки ({rpCount})";
            ShadersBadgeText = $"Шейдеры ({shadersCount})";
        }

        var allItems = new List<ContentTileItemViewModel>(required);
        allItems.AddRange(optional);
        _ = Content.LoadContentIconsAsync(allItems, SelectedContentKind);
    }

    [RelayCommand]
    public async Task SelectContentKindAsync(string? tag)
    {
        SelectedContentKind = tag switch
        {
            "rp" => ContentKind.ResourcePacks,
            "shaders" => ContentKind.Shaders,
            "worlds" => ContentKind.Worlds,
            _ => ContentKind.Mods
        };

        await RefreshContentAsync(false);
    }

    [RelayCommand]
    public async Task ToggleContentItemAsync(ContentTileItemViewModel? item)
    {
        if (item is null || Content is null) return;

        try
        {
            await Content.ToggleContentItemAsync(item, Account);
            await RefreshContentAsync(false);
        }
        catch (System.Exception ex)
        {
            Dialogs?.ShowMessage("Не удалось выполнить действие: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    [RelayCommand]
    public async Task ImportContentFilesAsync(IEnumerable<string>? files)
    {
        if (files is null || SelectedInstance is null || Content is null)
        {
            Dialogs?.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        var report = await Content.ImportFilesAsync(SelectedInstance, files);
        AppendLog("Импорт: " + report.Replace("\n", "; "));
        await RefreshContentAsync(false);

        var severity = report.Contains("Ошибки") ? MessageSeverity.Warning : MessageSeverity.Info;
        Dialogs?.ShowMessage(report, "Импорт файлов", severity);
    }

    [RelayCommand]
    public void OpenContentFolder()
    {
        if (SelectedInstance is null || InstanceService is null) return;
        try
        {
            var dir = SelectedContentKind switch
            {
                ContentKind.ResourcePacks => InstanceService.ResourcePacksDir(SelectedInstance),
                ContentKind.Shaders => InstanceService.ShaderPacksDir(SelectedInstance),
                ContentKind.Worlds => InstanceService.SavesDir(SelectedInstance),
                _ => InstanceService.ModsDir(SelectedInstance)
            };
            InstanceService.OpenFolder(dir);
        }
        catch (System.Exception ex)
        {
            AppendLog(ex.Message);
        }
    }
}
