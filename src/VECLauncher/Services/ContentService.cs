using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VECLauncher.Models;
using VECLauncher.ViewModels;
using IOPath = System.IO.Path;

namespace VECLauncher.Services;

public class ContentService : IContentService
{
    private readonly HttpClient _http;
    private readonly IInstanceService _instancesService;
    private readonly IImageCacheService _imageCache;

    private static List<BuildManifestFile> _cachedServerManifest = new();
    private static DateTime _lastManifestFetch = DateTime.MinValue;

    private static readonly ConcurrentDictionary<string, string> _modNameCache = new();
    private static readonly ConcurrentDictionary<string, string?> _modIdCache = new();
    private static readonly ConcurrentDictionary<string, BitmapImage?> _modIconCache = new();
    private static readonly ConcurrentDictionary<string, (string? title, BitmapImage? icon, BitmapImage? banner, string? description, string? author)> _modrinthProjectCache = new();

    private static readonly Regex _rxVersionSuffix = new(
        @"[-_](mc)?(1\.|forge|fabric|neoforge|quilt|sl|babric|legacy)[^""]*",
        RegexOptions.Compiled);
    private static readonly Regex _rxVersionNumber = new(
        @"[-_]v?\d+\.\d+[^""]*",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> _knownModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alexsmobs"] = "Alex's Mobs",
        ["ars_nouveau"] = "Ars Nouveau",
        ["baublemounts"] = "Baubles Mounts",
        ["appleskin"] = "AppleSkin",
        ["backpacked"] = "Backpacked",
        ["balm"] = "Balm",
        ["autoreglib"] = "AutoRegLib",
        ["architectury"] = "Architectury",
        ["bbs"] = "Better Bed spawning",
        ["beylewither"] = "Beyle Wither",
        ["curios"] = "Curios API",
        ["jei"] = "Just Enough Items",
        ["crafttweaker"] = "CraftTweaker",
        ["create"] = "Create",
        ["createaddition"] = "Create Addition",
        ["mekanism"] = "Mekanism",
        ["tconstruct"] = "Tinkers Construct",
        ["mantle"] = "Mantle",
        ["thermal"] = "Thermal Expansion",
        ["botania"] = "Botania",
        ["thaumcraft"] = "Thaumcraft",
        ["appliedenergistics2"] = "Applied Energistics 2",
        ["ae2"] = "Applied Energistics 2",
        ["ironchest"] = "Iron Chests",
        ["ironfurnaces"] = "Iron Furnaces",
        ["computercraft"] = "Computer Craft",
        ["xnet"] = "XNet",
        ["rftools"] = "RFTools",
        ["rftoolsbase"] = "RFTools Base",
        ["refinedstorage"] = "Refined Storage",
        ["rabbitry"] = "Rabbitry",
        ["morpheus"] = "Morpheus",
        ["cloth_config"] = "Cloth Config",
        ["patchouli"] = "Patchouli",
        ["obookshelf"] = "O Bookshelf",
        ["bookshelf"] = "Bookshelf",
        ["reccomplex"] = "Recurrent Complex",
        ["llibrary"] = "LLibrary",
        ["obfuscate"] = "Obfuscate",
        ["placebo"] = "Placebo",
        ["blueprint"] = "Blueprint",
        ["environmental"] = "Environmental",
        ["blue_skies"] = "Blue Skies",
        ["iceandfire"] = "Ice and Fire",
        ["zawa"] = "Zawa",
        ["minecolonies"] = "MineColonies",
        ["valhelsia_structures"] = "Valhelsia Structures",
        ["titanium"] = "Titanium",
        ["silence_lib"] = "Silence Lib",
        ["sophisticated_core"] = "Sophisticated Core",
        ["sophisticated_backpacks"] = "Sophisticated Backpacks",
        ["sophisticated_storage"] = "Sophisticated Storage",
        ["geckolib"] = "GeckoLib",
        ["geckolib_forge"] = "GeckoLib",
        ["croptopia"] = "Croptopia",
        ["farmers_delight"] = "Farmer's Delight",
        ["farmersdelight"] = "Farmer's Delight",
        ["elevatorid"] = "Elevator Mod",
        ["lightOverlay"] = "Light Overlay",
        ["lightoverlay"] = "Light Overlay",
        ["worldedit"] = "WorldEdit",
        ["world_guard"] = "World Guard",
        ["journeymap"] = "JourneyMap",
        ["xaeros_minimap"] = "Xaero's Minimap",
        ["xaeros_world_map"] = "Xaero's World Map",
        ["travelers_titles"] = "Traveler's Titles",
        ["explorify"] = "Explorify",
        ["village_spawn_point"] = "Village Spawn Point",
        ["kotlinforforge"] = "Kotlin for Forge",
        ["bootstrap"] = "Bootstrap",
        ["moonlight"] = "Moonlight Lib",
        ["moonlightlib"] = "Moonlight Lib",
        ["selene"] = "Moonlight Lib",
        ["medievalgiant"] = "Medieval Giant",
        ["handcrafted"] = "Handcrafted",
        ["chipped"] = "Chipped",
        ["decoration_delight"] = "Decoration Delight",
        ["resourceful_lib"] = "Resourceful Lib",
        ["resourcefullib"] = "Resourceful Lib",
        ["resourceful_tools"] = "Resourceful Tools",
        ["chisels_and_bits"] = "Chisels and Bits",
        ["ctoverclocked"] = "CraftTweaker Overclocked",
        ["toughasnails"] = "Tough As Nails",
        ["serene_seasons"] = "Serene Seasons",
        ["comforts"] = "Comforts",
        ["carry_on"] = "Carry On",
        ["carryon"] = "Carry On",
        ["inventory_hud"] = "Inventory HUD",
        ["inventoryhud"] = "Inventory HUD",
        ["neapolitan"] = "Neapolitan",
        ["supplementaries"] = "Supplementaries",
        ["quark"] = "Quark",
        ["quark_oddities"] = "Quark Oddities"
    };

    public ContentService(HttpClient http, IInstanceService instancesService)
        : this(http, instancesService, new ImageCacheService())
    {
    }

    public ContentService(HttpClient http, IInstanceService instancesService, IImageCacheService imageCache)
    {
        _http = http;
        _instancesService = instancesService;
        _imageCache = imageCache;
    }

    public void SetPreloadedManifest(List<BuildManifestFile> files)
    {
        if (files != null && files.Count > 0)
        {
            _cachedServerManifest = files;
            _lastManifestFetch = DateTime.UtcNow;
        }
    }

    public async Task<List<BuildManifestFile>> FetchBuildManifestAsync(MinecraftAccount? account, bool force = false)
    {
        if (!force && _cachedServerManifest.Count > 0 && (DateTime.UtcNow - _lastManifestFetch).TotalSeconds < 8)
        {
            return _cachedServerManifest;
        }

        var candidates = new List<string> { VecAuthService.DefaultVecServerUrl };
        if (!string.IsNullOrWhiteSpace(account?.ServerUrl))
            candidates.Add(account.ServerUrl.TrimEnd('/'));
        candidates.Add(LauncherConfig.DevelopmentWebServerUrl);
        candidates.Add(LauncherConfig.ProductionWebServerUrl);

        foreach (var serverUrl in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = await _http.GetStringAsync($"{serverUrl}/api/builds/vec-fabric-1-21-11/manifest");
                var res = JsonSerializer.Deserialize<BuildManifestResponse>(json);
                if (res?.Files != null && res.Files.Count > 0)
                {
                    _cachedServerManifest = res.Files;
                    _lastManifestFetch = DateTime.UtcNow;
                    return _cachedServerManifest;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Загрузка манифеста сборки: " + ex.Message);
            }
        }

        return _cachedServerManifest;
    }

    public (int mods, int rp, int shaders) GetCategoryCounts(List<BuildManifestFile> manifest)
    {
        if (manifest is null || manifest.Count == 0) return (0, 0, 0);

        var mods = manifest.Count(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase));
        var rp = manifest.Count(f => f.Path.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(f.Category, "ResourcePacks", StringComparison.OrdinalIgnoreCase));
        var shaders = manifest.Count(f => f.Path.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(f.Category, "Shaders", StringComparison.OrdinalIgnoreCase));

        return (mods, rp, shaders);
    }

    private string CurrentContentDir(GameInstance instance, ContentKind kind)
    {
        return kind switch
        {
            ContentKind.ResourcePacks => _instancesService.ResourcePacksDir(instance),
            ContentKind.Shaders => _instancesService.ShaderPacksDir(instance),
            ContentKind.Worlds => _instancesService.SavesDir(instance),
            _ => _instancesService.ModsDir(instance)
        };
    }

    public async Task<(List<ContentTileItemViewModel> Required, List<ContentTileItemViewModel> Optional)> LoadContentItemsAsync(
        GameInstance instance,
        ContentKind kind,
        MinecraftAccount? account,
        bool forceManifest = false)
    {
        var requiredItems = new List<ContentTileItemViewModel>();
        var optionalItems = new List<ContentTileItemViewModel>();

        var dir = CurrentContentDir(instance, kind);
        Directory.CreateDirectory(dir);

        try
        {
            if (kind == ContentKind.Worlds)
            {
                foreach (var d in new DirectoryInfo(dir).GetDirectories().OrderByDescending(x => x.LastWriteTime))
                {
                    long size = 0;
                    try
                    {
                        size = d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex.Message);
                    }

                    optionalItems.Add(new ContentTileItemViewModel
                    {
                        Name = d.Name,
                        Description = "Локальное сохранение мира",
                        Category = "МИР",
                        Info = $"{MainWindowViewModel.Human(size)} · изменён {d.LastWriteTime:dd.MM.yyyy HH:mm}",
                        Author = "Одиночная игра",
                        VersionTag = "Мир",
                        SizeTag = MainWindowViewModel.Human(size),
                        Path = d.FullName,
                        IsRequired = false,
                        ToggleText = "",
                        IconLetter = d.Name.Length > 0 ? d.Name[..1].ToUpperInvariant() : "W"
                    });
                }
            }
            else
            {
                var manifestFiles = await FetchBuildManifestAsync(account, forceManifest);
                var handledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                IEnumerable<BuildManifestFile> targetManifest = kind switch
                {
                    ContentKind.Mods => manifestFiles.Where(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)),
                    ContentKind.ResourcePacks => manifestFiles.Where(f => f.Path.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) ||
                                                                           string.Equals(f.Category, "ResourcePacks", StringComparison.OrdinalIgnoreCase)),
                    ContentKind.Shaders => manifestFiles.Where(f => f.Path.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase) ||
                                                                     string.Equals(f.Category, "Shaders", StringComparison.OrdinalIgnoreCase)),
                    _ => Enumerable.Empty<BuildManifestFile>()
                };

                foreach (var srv in targetManifest)
                {
                    var fileName = string.IsNullOrWhiteSpace(srv.FileName) ? IOPath.GetFileName(srv.Path) : srv.FileName;
                    handledNames.Add(fileName);

                    var localPath = IOPath.Combine(dir, fileName);
                    var disabledPath = localPath + ".disabled";
                    bool isPresent = File.Exists(localPath);
                    bool isDisabled = File.Exists(disabledPath);

                    string displayName = !string.IsNullOrWhiteSpace(srv.DisplayName) ? srv.DisplayName : fileName;
                    string info = $"{MainWindowViewModel.Human(srv.Size)} · {srv.Path}";
                    string description = !string.IsNullOrWhiteSpace(srv.Description)
                        ? srv.Description
                        : "Компонент официальной сборки VEC";

                    string category = !string.IsNullOrWhiteSpace(srv.Category)
                        ? srv.Category.ToUpperInvariant()
                        : "МОД";

                    string toggleText;
                    string targetPath;

                    if (isPresent || isDisabled)
                    {
                        targetPath = isPresent ? localPath : disabledPath;
                        toggleText = isPresent ? "Выключить" : "Включить";
                    }
                    else
                    {
                        targetPath = localPath;
                        toggleText = "Скачать";
                    }

                    var item = new ContentTileItemViewModel
                    {
                        Name = displayName,
                        Description = description,
                        Category = category,
                        Info = info,
                        Author = "Сервер VEC",
                        VersionTag = $"{instance.LoaderDisplay} {instance.McVersion}",
                        SizeTag = MainWindowViewModel.Human(srv.Size),
                        Path = targetPath,
                        IsRequired = srv.IsRequired,
                        IsServerItem = true,
                        ModrinthSlug = srv.ModrinthSlug,
                        IconUrl = srv.IconUrl,
                        BannerUrl = srv.BannerUrl,
                        IconImage = _imageCache.TryGetCached(srv.IconUrl),
                        BannerImage = _imageCache.TryGetCached(srv.BannerUrl),
                        ToggleText = toggleText,
                        IconLetter = displayName.Length > 0 ? displayName[..1].ToUpperInvariant() : "V"
                    };

                    if (srv.IsRequired)
                    {
                        requiredItems.Add(item);
                    }
                    else
                    {
                        optionalItems.Add(item);
                    }
                }

                var patterns = kind == ContentKind.Mods
                    ? new[] { "*.jar", "*.jar.disabled" }
                    : new[] { "*.zip", "*.zip.disabled" };

                var localFiles = patterns
                    .SelectMany(pat => new DirectoryInfo(dir).GetFiles(pat))
                    .DistinctBy(f => f.FullName)
                    .OrderBy(f => f.Name)
                    .ToList();

                foreach (var f in localFiles)
                {
                    var pureName = f.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? f.Name[..^".disabled".Length]
                        : f.Name;

                    if (handledNames.Contains(pureName)) continue;

                    var enabled = !f.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var display = enabled ? IOPath.GetFileNameWithoutExtension(f.Name) : IOPath.GetFileNameWithoutExtension(pureName) + " (выключен)";

                    optionalItems.Add(new ContentTileItemViewModel
                    {
                        Name = display,
                        Description = "Локальный пользовательский файл",
                        Category = "ЛОКАЛЬНЫЙ",
                        Info = $"{MainWindowViewModel.Human(f.Length)} · {f.LastWriteTime:dd.MM.yyyy}",
                        Author = "Локальный",
                        VersionTag = $"{instance.LoaderDisplay} {instance.McVersion}",
                        SizeTag = MainWindowViewModel.Human(f.Length),
                        Path = f.FullName,
                        IsRequired = false,
                        ToggleText = enabled ? "Выключить" : "Включить",
                        IconLetter = display.Length > 0 ? display[..1].ToUpperInvariant() : "L"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Чтение содержимого сборки: " + ex.Message);
        }

        return (requiredItems, optionalItems);
    }

    public async Task ToggleContentItemAsync(
        ContentTileItemViewModel item,
        MinecraftAccount? account,
        List<BuildManifestFile>? catalog = null)
    {
        var path = item.Path;
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path) && !File.Exists(path + ".disabled"))
        {
            var targetName = IOPath.GetFileName(path);
            var manifest = catalog ?? _cachedServerManifest;
            var srv = manifest.FirstOrDefault(f =>
                string.Equals(f.FileName, targetName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(IOPath.GetFileName(f.Path), targetName, StringComparison.OrdinalIgnoreCase));

            if (srv != null)
            {
                var serverUrl = !string.IsNullOrWhiteSpace(account?.ServerUrl)
                    ? account.ServerUrl.TrimEnd('/')
                    : VecAuthService.DefaultVecServerUrl;

                var downloadUrl = !string.IsNullOrWhiteSpace(srv.DownloadUrl)
                    ? (srv.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? srv.DownloadUrl
                        : $"{serverUrl}/{srv.DownloadUrl.TrimStart('/')}")
                    : $"{serverUrl}/builds/vec-fabric-1-21-11/{srv.Path.TrimStart('/')}";

                var bytes = await _http.GetByteArrayAsync(downloadUrl);

                var folder = IOPath.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                await File.WriteAllBytesAsync(path, bytes);
                item.ToggleText = "Выключить";
                return;
            }
        }

        var target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path + ".disabled";

        if (File.Exists(target)) File.Delete(target);
        if (File.Exists(path)) File.Move(path, target);

        item.Path = target;
        item.ToggleText = target.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? "Включить" : "Выключить";
    }

    public async Task<string> ImportFilesAsync(GameInstance instance, IEnumerable<string> paths)
    {
        _instancesService.EnsureFolders(instance);

        var ok = 0;
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var src in paths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var worldDst = IOPath.Combine(_instancesService.SavesDir(instance), IOPath.GetFileName(src));
                    if (Directory.Exists(worldDst))
                    {
                        skipped.Add(IOPath.GetFileName(src) + " (уже есть)");
                        continue;
                    }
                    CopyDirectory(src, worldDst);
                    ok++;
                    continue;
                }

                if (!File.Exists(src)) continue;

                var ext = IOPath.GetExtension(src).ToLowerInvariant();
                var name = IOPath.GetFileName(src);

                string dstDir;
                if (ext == ".jar")
                {
                    dstDir = _instancesService.ModsDir(instance);
                }
                else if (ext == ".zip")
                {
                    dstDir = LooksLikeShaderPack(src)
                        ? _instancesService.ShaderPacksDir(instance)
                        : _instancesService.ResourcePacksDir(instance);
                }
                else
                {
                    skipped.Add(name + " (неизвестный тип)");
                    continue;
                }

                Directory.CreateDirectory(dstDir);
                var dst = IOPath.Combine(dstDir, name);
                if (File.Exists(dst))
                {
                    skipped.Add(name + " (уже есть)");
                    continue;
                }

                File.Copy(src, dst);
                ok++;
            }
            catch (Exception ex)
            {
                failed.Add(IOPath.GetFileName(src) + ": " + ex.Message);
            }
        }

        var report = $"Добавлено: {ok}";
        if (skipped.Count > 0) report += $"\nПропущено: {string.Join(", ", skipped)}";
        if (failed.Count > 0) report += $"\nОшибки: {string.Join(", ", failed)}";

        return await Task.FromResult(report);
    }

    public async Task LoadContentIconsAsync(List<ContentTileItemViewModel> items, ContentKind kind)
    {
        if (items.Count == 0) return;
        var itemsCopy = items.ToList();

        foreach (var item in itemsCopy)
        {
            try
            {
                if (!string.IsNullOrEmpty(item.IconUrl) && item.IconImage is null)
                {
                    var iconBmp = await _imageCache.GetAsync(item.IconUrl, _http, decodeWidth: 128);
                    if (iconBmp != null) item.IconImage = iconBmp;
                }

                if (!string.IsNullOrEmpty(item.BannerUrl) && item.BannerImage is null)
                {
                    var bannerBmp = await _imageCache.GetAsync(item.BannerUrl, _http, decodeWidth: 0);
                    if (bannerBmp != null) item.BannerImage = bannerBmp;
                }

                if (item.IsServerItem && item.IconImage is not null && item.BannerImage is not null)
                    continue;

                var filePath = item.Path;
                if (string.IsNullOrEmpty(filePath)) continue;

                BitmapImage? remoteIcon = item.IconImage;
                BitmapImage? remoteBanner = item.BannerImage;
                string? remoteName = null;
                string? remoteDescription = null;

                if (kind == ContentKind.Mods)
                {
                    var jarName = IOPath.GetFileName(filePath);
                    var isJar = filePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                filePath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

                    if (isJar && File.Exists(filePath))
                    {
                        var modInfo = ExtractModInfo(filePath);
                        if (!string.IsNullOrEmpty(modInfo.modName)) remoteName = modInfo.modName;
                        if (modInfo.icon is not null && remoteIcon is null) remoteIcon = modInfo.icon;

                        var slugToQuery = !string.IsNullOrWhiteSpace(item.ModrinthSlug)
                            ? item.ModrinthSlug
                            : (!string.IsNullOrWhiteSpace(modInfo.modId) ? modInfo.modId : CleanSlugFromFilename(jarName));

                        if (!string.IsNullOrWhiteSpace(slugToQuery) && (remoteIcon == null || remoteBanner == null))
                        {
                            var mr = await FetchModrinthProjectAsync(slugToQuery, "mod");
                            if (mr.icon != null && remoteIcon == null) remoteIcon = mr.icon;
                            if (mr.banner != null && remoteBanner == null) remoteBanner = mr.banner;
                            if (!string.IsNullOrWhiteSpace(mr.title) && !item.IsServerItem) remoteName = mr.title;
                            if (!string.IsNullOrWhiteSpace(mr.description)) remoteDescription = mr.description;
                        }
                    }
                }
                else if (kind == ContentKind.ResourcePacks)
                {
                    var zipName = IOPath.GetFileName(filePath);
                    var isZip = filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                                filePath.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase);

                    if (isZip && File.Exists(filePath))
                    {
                        var packInfo = ExtractPackInfo(filePath);
                        if (!string.IsNullOrEmpty(packInfo.name) && !item.IsServerItem) remoteName = packInfo.name;

                        var slugToQuery = !string.IsNullOrWhiteSpace(item.ModrinthSlug)
                            ? item.ModrinthSlug
                            : CleanSlugFromFilename(zipName);

                        if (!string.IsNullOrWhiteSpace(slugToQuery) && (remoteIcon == null || remoteBanner == null))
                        {
                            var mr = await FetchModrinthProjectAsync(slugToQuery, "resourcepack");
                            if (mr.icon != null && remoteIcon == null) remoteIcon = mr.icon;
                            if (mr.banner != null && remoteBanner == null) remoteBanner = mr.banner;
                            if (!string.IsNullOrWhiteSpace(mr.title) && !item.IsServerItem) remoteName = mr.title;
                            if (!string.IsNullOrWhiteSpace(mr.description)) remoteDescription = mr.description;
                        }
                    }
                }
                else if (kind == ContentKind.Shaders)
                {
                    var zipName = IOPath.GetFileName(filePath);
                    var slugToQuery = !string.IsNullOrWhiteSpace(item.ModrinthSlug)
                        ? item.ModrinthSlug
                        : CleanSlugFromFilename(zipName);

                    if (!string.IsNullOrWhiteSpace(slugToQuery) && (remoteIcon == null || remoteBanner == null))
                    {
                        var mr = await FetchModrinthProjectAsync(slugToQuery, "shader");
                        if (mr.icon != null && remoteIcon == null) remoteIcon = mr.icon;
                        if (mr.banner != null && remoteBanner == null) remoteBanner = mr.banner;
                        if (!string.IsNullOrWhiteSpace(mr.title) && !item.IsServerItem) remoteName = mr.title;
                        if (!string.IsNullOrWhiteSpace(mr.description)) remoteDescription = mr.description;
                    }
                }

                if (remoteIcon != null && item.IconImage == null) item.IconImage = remoteIcon;
                if (remoteBanner != null && item.BannerImage == null) item.BannerImage = remoteBanner;
                if (!string.IsNullOrEmpty(remoteName) && !item.IsServerItem) item.Name = remoteName;
                if (!string.IsNullOrEmpty(remoteDescription) && string.IsNullOrEmpty(item.Description)) item.Description = remoteDescription;
            }
            catch (Exception ex)
            {
                Log.Warn($"LoadContentIconsAsync error for {item.Name}: {ex.Message}");
            }
        }
    }

    private async Task<BitmapImage?> DownloadIconAsync(string url, int decodeWidth = 128)
    {
        return await _imageCache.GetAsync(url, _http, decodeWidth);
    }

    private (string modName, string? modId, BitmapImage? icon) ExtractModInfo(string jarPath)
    {
        if (_modNameCache.TryGetValue(jarPath, out var cachedName))
        {
            _modIconCache.TryGetValue(jarPath, out var cachedIcon);
            _modIdCache.TryGetValue(jarPath, out var cachedId);
            return (cachedName, cachedId, cachedIcon);
        }

        string name = IOPath.GetFileNameWithoutExtension(jarPath);
        string? modId = null;
        BitmapImage? icon = null;

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var stream = fabricEntry.Open();
                var doc = JsonNode.Parse(stream);
                name = doc?["name"]?.GetValue<string>() ?? name;
                modId = doc?["id"]?.GetValue<string>();

                var iconPath = doc?["icon"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var iconEntry = archive.GetEntry(iconPath);
                    if (iconEntry is not null)
                        icon = LoadIconFromEntry(iconEntry);
                }
            }
            else
            {
                var modsToml = archive.GetEntry("META-INF/mods.toml")
                              ?? archive.GetEntry("META-INF/neoforge.mods.toml");
                if (modsToml is not null)
                {
                    using var reader = new StreamReader(modsToml.Open());
                    var tomlText = reader.ReadToEnd();
                    var lines = tomlText.Split('\n');
                    bool inMods = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        var sectionCheck = trimmed.Split('#')[0].Trim();
                        if (sectionCheck == "[[mods]]" || sectionCheck.StartsWith("[[mods.")) inMods = true;
                        else if (sectionCheck.StartsWith("[")) inMods = false;

                        if (inMods && trimmed.StartsWith("displayName"))
                        {
                            name = CleanTomlValue(trimmed);
                        }
                        if (inMods && trimmed.StartsWith("modId"))
                        {
                            modId = CleanTomlValue(trimmed);
                        }
                        if (inMods && trimmed.StartsWith("logoFile"))
                        {
                            var logoPath = CleanTomlValue(trimmed);
                            var logoEntry = archive.GetEntry(logoPath);
                            if (logoEntry is not null)
                                icon = LoadIconFromEntry(logoEntry);
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        _modNameCache[jarPath] = name;
        _modIconCache[jarPath] = icon;
        if (modId != null) _modIdCache[jarPath] = modId;
        return (name, modId, icon);
    }

    private static BitmapImage? LoadIconFromEntry(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private (string name, string? slug) ExtractPackInfo(string zipPath)
    {
        string name = IOPath.GetFileNameWithoutExtension(zipPath);
        string? slug = null;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var packMeta = archive.GetEntry("pack.mcmeta");
            if (packMeta is not null)
            {
                using var reader = new StreamReader(packMeta.Open());
                var doc = JsonNode.Parse(reader.ReadToEnd());
                name = doc?["pack"]?["description"]?.GetValue<string>() ?? name;
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
        return (name, slug);
    }

    private async Task<(string? title, BitmapImage? icon, BitmapImage? banner, string? description, string? author)> FetchModrinthProjectAsync(string query, string projectType = "mod")
    {
        var cleanSlug = query.Trim().Replace('_', '-').Trim('-');
        var cacheKey = $"{projectType}:{cleanSlug}";
        if (_modrinthProjectCache.TryGetValue(cacheKey, out var cached)) return cached;

        string? title = null;
        BitmapImage? icon = null;
        BitmapImage? banner = null;
        string? description = null;
        string? author = null;

        try
        {
            var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(cleanSlug)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "VEC Launcher/1.0");
            using var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonNode.Parse(json);
                title = doc?["title"]?.GetValue<string>();
                description = doc?["description"]?.GetValue<string>();
                var iconUrl = doc?["icon_url"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconUrl))
                    icon = await DownloadIconAsync(iconUrl);

                var gallery = doc?["gallery"]?.AsArray();
                string? bannerUrl = null;
                if (gallery is not null && gallery.Count > 0)
                {
                    var featured = gallery.FirstOrDefault(g => g?["featured"]?.GetValue<bool>() == true);
                    bannerUrl = (featured ?? gallery[0])?["url"]?.GetValue<string>();
                }
                if (!string.IsNullOrEmpty(bannerUrl))
                    banner = await DownloadIconAsync(bannerUrl, 0);

                author = doc?["organization"]?.GetValue<string>();
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        var result = (title, icon, banner, description, author);
        _modrinthProjectCache[cacheKey] = result;
        return result;
    }

    private static string CleanSlugFromFilename(string filename)
    {
        var slug = IOPath.GetFileNameWithoutExtension(filename).ToLower();
        slug = _rxVersionSuffix.Replace(slug, "");
        slug = _rxVersionNumber.Replace(slug, "");
        return slug.Replace('_', '-').Trim('-');
    }

    private static string CleanTomlValue(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return "";
        var val = line[(eq + 1)..].Trim();
        bool inQuote = false;
        int hashIdx = -1;
        for (int i = 0; i < val.Length; i++)
        {
            if (val[i] == '"') inQuote = !inQuote;
            if (!inQuote && val[i] == '#') { hashIdx = i; break; }
        }
        if (hashIdx >= 0) val = val[..hashIdx].TrimEnd();
        return val.Trim('"', '\'', ' ', '\r', '\n');
    }

    private static bool LooksLikeShaderPack(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(entry =>
                entry.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("/shaders/", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);

        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, IOPath.Combine(dst, IOPath.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, IOPath.Combine(dst, IOPath.GetFileName(dir)));
    }
}
