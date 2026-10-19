using System.Collections.Generic;
using System.Threading.Tasks;
using VECLauncher.Models;
using VECLauncher.ViewModels;

namespace VECLauncher.Services;

public interface IContentService
{
    void SetPreloadedManifest(List<BuildManifestFile> files);
    Task<List<BuildManifestFile>> FetchBuildManifestAsync(MinecraftAccount? account, bool force = false);
    Task<(List<ContentTileItemViewModel> Required, List<ContentTileItemViewModel> Optional)> LoadContentItemsAsync(
        GameInstance instance,
        ContentKind kind,
        MinecraftAccount? account,
        bool forceManifest = false);
    Task ToggleContentItemAsync(
        ContentTileItemViewModel item,
        MinecraftAccount? account,
        List<BuildManifestFile>? catalog = null);
    Task<string> ImportFilesAsync(GameInstance instance, IEnumerable<string> paths);
    Task LoadContentIconsAsync(List<ContentTileItemViewModel> items, ContentKind kind);
    (int mods, int rp, int shaders) GetCategoryCounts(List<BuildManifestFile> manifest);
}
