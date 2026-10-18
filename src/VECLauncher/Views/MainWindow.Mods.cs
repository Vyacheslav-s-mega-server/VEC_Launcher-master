using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private List<BuildManifestFile> _approvedCatalog = new();
    private ContentKind _contentKind = ContentKind.Mods;

    private void ContentKind_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _contentKind = (sender as FrameworkElement)?.Tag?.ToString() switch
        {
            "rp" => ContentKind.ResourcePacks,
            "shaders" => ContentKind.Shaders,
            "worlds" => ContentKind.Worlds,
            _ => ContentKind.Mods
        };

        RefreshContent();
    }

    private string CurrentContentDir()
    {
        if (_selectedInstance is null) return "";

        return _contentKind switch
        {
            ContentKind.ResourcePacks => _instancesService.ResourcePacksDir(_selectedInstance),
            ContentKind.Shaders => _instancesService.ShaderPacksDir(_selectedInstance),
            ContentKind.Worlds => _instancesService.SavesDir(_selectedInstance),
            _ => _instancesService.ModsDir(_selectedInstance)
        };
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

    private static string CleanSlugFromFilename(string filename)
    {
        var slug = IOPath.GetFileNameWithoutExtension(filename).ToLower();
        slug = _rxVersionSuffix.Replace(slug, "");
        slug = _rxVersionNumber.Replace(slug, "");
        return slug.Replace('_', '-').Trim('-');
    }

    private static string FormatModName(string slug)
    {
        if (_knownModNames.TryGetValue(slug, out var known)) return known;
        return string.Join(" ", slug.Split('_', '-').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    private async Task<BitmapImage?> DownloadIconAsync(string url, string cacheKey)
    {
        try
        {
            var data = await _iconHttp.GetByteArrayAsync(url);
            using var ms = new MemoryStream(data);
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

    private static List<BuildManifestFile> _cachedServerManifest = new();
    private static DateTime _lastManifestFetch = DateTime.MinValue;
    private DispatcherTimer? _contentPollingTimer;

    public static void SetPreloadedManifest(List<BuildManifestFile> files)
    {
        if (files != null && files.Count > 0)
        {
            _cachedServerManifest = files;
            _lastManifestFetch = DateTime.UtcNow;
        }
    }

    private void StartContentPolling()
    {
        if (_contentPollingTimer != null) return;
        _contentPollingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _contentPollingTimer.Tick += (s, e) =>
        {
            if (PageContent?.Visibility == Visibility.Visible && _selectedInstance != null)
            {
                RefreshContent(false);
            }
        };
        _contentPollingTimer.Start();
    }

    private async Task<List<BuildManifestFile>> FetchBuildManifestAsync(bool force = false)
    {
        if (!force && _cachedServerManifest.Count > 0 && (DateTime.UtcNow - _lastManifestFetch).TotalSeconds < 8)
        {
            _approvedCatalog = _cachedServerManifest;
            return _approvedCatalog;
        }

        var candidates = new List<string> { VecAuthService.DefaultVecServerUrl };
        if (!string.IsNullOrWhiteSpace(_account?.ServerUrl))
            candidates.Add(_account.ServerUrl.TrimEnd('/'));
        candidates.Add(LauncherConfig.DevelopmentWebServerUrl);
        candidates.Add(LauncherConfig.ProductionWebServerUrl);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        foreach (var serverUrl in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = await http.GetStringAsync($"{serverUrl}/api/builds/vec-fabric-1-21-11/manifest");
                var res = System.Text.Json.JsonSerializer.Deserialize<BuildManifestResponse>(json);
                if (res?.Files != null && res.Files.Count > 0)
                {
                    _cachedServerManifest = res.Files;
                    _lastManifestFetch = DateTime.UtcNow;
                    _approvedCatalog = _cachedServerManifest;
                    return _approvedCatalog;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Загрузка манифеста сборки: " + ex.Message);
            }
        }

        if (_cachedServerManifest.Count > 0)
        {
            _approvedCatalog = _cachedServerManifest;
        }
        return _approvedCatalog;
    }

    private async void RefreshContent(bool force = false)
    {
        if (_selectedInstance is null)
        {
            ItemsRequired.ItemsSource = null;
            ItemsOptional.ItemsSource = null;
            SecRequired.Visibility = Visibility.Collapsed;
            SecOptional.Visibility = Visibility.Collapsed;
            return;
        }

        TxtContentSubtitle.Text = $"Сборка «{_selectedInstance.Name}» · {_selectedInstance.McVersion} · {_selectedInstance.LoaderDisplay}";

        var dir = CurrentContentDir();
        Directory.CreateDirectory(dir);

        var requiredItems = new List<ContentTileItem>();
        var optionalItems = new List<ContentTileItem>();

        try
        {
            if (_contentKind == ContentKind.Worlds)
            {
                foreach (var d in new DirectoryInfo(dir).GetDirectories().OrderByDescending(x => x.LastWriteTime))
                {
                    long size = 0;
                    try { size = d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch (Exception ex) { Log.Warn(ex.Message); }

                    optionalItems.Add(new ContentTileItem
                    {
                        Name = d.Name,
                        Description = "Локальное сохранение мира",
                        Category = "МИР",
                        Info = $"{Human(size)} · изменён {d.LastWriteTime:dd.MM.yyyy HH:mm}",
                        Author = "Одиночная игра",
                        VersionTag = "Мир",
                        SizeTag = Human(size),
                        Path = d.FullName,
                        IsRequired = false,
                        ToggleText = "",
                        IconLetter = d.Name.Length > 0 ? d.Name[..1].ToUpperInvariant() : "W"
                    });
                }
            }
            else
            {
                var manifestFiles = await FetchBuildManifestAsync(force);
                var handledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (manifestFiles.Count > 0)
                {
                    CtMods.Content = $"Моды ({manifestFiles.Count(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))})";
                    CtRp.Content = $"Ресурспаки ({manifestFiles.Count(f => f.Path.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Category, "ResourcePacks", StringComparison.OrdinalIgnoreCase))})";
                    CtShaders.Content = $"Шейдеры ({manifestFiles.Count(f => f.Path.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Category, "Shaders", StringComparison.OrdinalIgnoreCase))})";
                }

                IEnumerable<BuildManifestFile> targetManifest = _contentKind switch
                {
                    ContentKind.Mods => manifestFiles.Where(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)),
                    ContentKind.ResourcePacks => manifestFiles.Where(f => f.Path.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Category, "ResourcePacks", StringComparison.OrdinalIgnoreCase)),
                    ContentKind.Shaders => manifestFiles.Where(f => f.Path.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Category, "Shaders", StringComparison.OrdinalIgnoreCase)),
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
                    string info = $"{Human(srv.Size)} · {srv.Path}";
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

                    BitmapImage? directIcon = null;
                    if (!string.IsNullOrWhiteSpace(srv.IconUrl))
                    {
                        try
                        {
                            directIcon = new BitmapImage();
                            directIcon.BeginInit();
                            directIcon.UriSource = new Uri(srv.IconUrl);
                            directIcon.CacheOption = BitmapCacheOption.OnLoad;
                            directIcon.EndInit();
                            directIcon.Freeze();
                        }
                        catch { directIcon = null; }
                    }

                    BitmapImage? directBanner = null;
                    if (!string.IsNullOrWhiteSpace(srv.BannerUrl))
                    {
                        try
                        {
                            directBanner = new BitmapImage();
                            directBanner.BeginInit();
                            directBanner.UriSource = new Uri(srv.BannerUrl);
                            directBanner.CacheOption = BitmapCacheOption.OnLoad;
                            directBanner.EndInit();
                            directBanner.Freeze();
                        }
                        catch { directBanner = null; }
                    }

                    var item = new ContentTileItem
                    {
                        Name = displayName,
                        Description = description,
                        Category = category,
                        Info = info,
                        Author = "Сервер VEC",
                        VersionTag = $"{_selectedInstance.LoaderDisplay} {_selectedInstance.McVersion}",
                        SizeTag = Human(srv.Size),
                        Path = targetPath,
                        IsRequired = srv.IsRequired,
                        IsServerItem = true,
                        ModrinthSlug = srv.ModrinthSlug,
                        IconImage = directIcon,
                        BannerImage = directBanner,
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

                var patterns = _contentKind == ContentKind.Mods
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

                    optionalItems.Add(new ContentTileItem
                    {
                        Name = display,
                        Description = "Локальный пользовательский файл",
                        Category = "ЛОКАЛЬНЫЙ",
                        Info = $"{Human(f.Length)} · {f.LastWriteTime:dd.MM.yyyy}",
                        Author = "Локальный",
                        VersionTag = $"{_selectedInstance.LoaderDisplay} {_selectedInstance.McVersion}",
                        SizeTag = Human(f.Length),
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

        ItemsRequired.ItemsSource = requiredItems;
        ItemsOptional.ItemsSource = optionalItems;

        SecRequired.Visibility = requiredItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SecOptional.Visibility = optionalItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var allItems = requiredItems.Concat(optionalItems).ToList();
        _ = LoadContentIconsAsync(allItems);
    }

    private async Task LoadContentIconsAsync(List<ContentTileItem> allItems)
    {
        if (allItems.Count == 0) return;
        var itemsCopy = allItems.ToList();

        foreach (var item in itemsCopy)
        {
            var filePath = item.Path;
            if (string.IsNullOrEmpty(filePath)) continue;

            if (item.IsServerItem && item.IconImage is not null && item.BannerImage is not null)
            {
                continue;
            }

            BitmapImage? remoteIcon = item.IconImage;
            BitmapImage? remoteBanner = item.BannerImage;
            string? remoteName = null;
            string? remoteDescription = null;
            string? remoteAuthor = null;

            try
            {
                if (_contentKind == ContentKind.Mods)
                {
                    _modIdCache.TryGetValue(filePath, out var modId);
                    var query = !string.IsNullOrEmpty(item.ModrinthSlug)
                        ? item.ModrinthSlug
                        : (!string.IsNullOrEmpty(modId) ? modId : CleanSlugFromFilename(filePath));
                    var (title, icon, banner, description, author) = await FetchModrinthProjectAsync(query, "mod");
                    if (icon is not null) remoteIcon = icon;
                    if (banner is not null) remoteBanner = banner;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                    else remoteName = FormatModName(CleanSlugFromFilename(filePath));
                    if (!string.IsNullOrEmpty(description)) remoteDescription = description;
                    if (!string.IsNullOrEmpty(author)) remoteAuthor = $"от {author}";
                }
                else if (_contentKind == ContentKind.ResourcePacks)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            using var archive = ZipFile.OpenRead(filePath);
                            var packPng = archive.GetEntry("pack.png");
                            if (packPng is not null) remoteIcon = LoadIconFromEntry(packPng);
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                    var query = !string.IsNullOrEmpty(item.ModrinthSlug)
                        ? item.ModrinthSlug
                        : CleanSlugFromFilename(filePath);
                    var (title, icon, banner, description, author) = await FetchModrinthProjectAsync(query, "resourcepack");
                    if (icon is not null && remoteIcon is null) remoteIcon = icon;
                    if (banner is not null) remoteBanner = banner;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                    if (!string.IsNullOrEmpty(description)) remoteDescription = description;
                    if (!string.IsNullOrEmpty(author)) remoteAuthor = $"от {author}";
                }
                else if (_contentKind == ContentKind.Shaders)
                {
                    var query = !string.IsNullOrEmpty(item.ModrinthSlug)
                        ? item.ModrinthSlug
                        : CleanSlugFromFilename(filePath);
                    var (title, icon, banner, description, author) = await FetchModrinthProjectAsync(query, "shader");
                    if (icon is not null) remoteIcon = icon;
                    if (banner is not null) remoteBanner = banner;
                    if (!string.IsNullOrEmpty(title)) remoteName = title;
                    if (!string.IsNullOrEmpty(description)) remoteDescription = description;
                    if (!string.IsNullOrEmpty(author)) remoteAuthor = $"от {author}";
                }

                if (item.IsServerItem)
                {
                    if (remoteIcon is not null && item.IconImage is null)
                    {
                        item.IconImage = remoteIcon;
                    }
                    if (remoteBanner is not null && item.BannerImage is null)
                    {
                        item.BannerImage = remoteBanner;
                    }
                    continue;
                }

                if (remoteName is not null && remoteName != item.Name)
                {
                    item.Name = remoteName;
                }
                if (remoteIcon is not null && remoteIcon != item.IconImage)
                {
                    item.IconImage = remoteIcon;
                }
                if (remoteBanner is not null && remoteBanner != item.BannerImage)
                {
                    item.BannerImage = remoteBanner;
                }
                if (remoteDescription is not null && remoteDescription != item.Description)
                {
                    item.Description = remoteDescription;
                }
                if (remoteAuthor is not null && remoteAuthor != item.Author)
                {
                    item.Author = remoteAuthor;
                }
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }
    }

    private void BtnRefreshContent_Click(object sender, RoutedEventArgs e) => RefreshContent(true);

    private void BtnOpenContentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is null) return;
        try { _instancesService.OpenFolder(CurrentContentDir()); }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private async void ContentToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;

        try
        {
            if (!File.Exists(path) && !File.Exists(path + ".disabled"))
            {
                var targetName = IOPath.GetFileName(path);
                var srv = _approvedCatalog.FirstOrDefault(f => string.Equals(f.FileName, targetName, StringComparison.OrdinalIgnoreCase) || string.Equals(IOPath.GetFileName(f.Path), targetName, StringComparison.OrdinalIgnoreCase));
                if (srv != null)
                {
                    var serverUrl = !string.IsNullOrWhiteSpace(_account?.ServerUrl)
                        ? _account.ServerUrl.TrimEnd('/')
                        : VecAuthService.DefaultVecServerUrl;

                    var downloadUrl = !string.IsNullOrWhiteSpace(srv.DownloadUrl)
                        ? (srv.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? srv.DownloadUrl : $"{serverUrl}/{srv.DownloadUrl.TrimStart('/')}")
                        : $"{serverUrl}/builds/vec-fabric-1-21-11/{srv.Path.TrimStart('/')}";

                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("VEC_Launcher/3.0 (contact@vec.kpvk.edu.kz)");
                    var bytes = await http.GetByteArrayAsync(downloadUrl);

                    var folder = IOPath.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                    await File.WriteAllBytesAsync(path, bytes);
                    RefreshContent();
                    return;
                }
            }

            var target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path + ".disabled";

            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(path)) File.Move(path, target);

            RefreshContent();
        }
        catch (Exception ex)
        {
            _dialog.ShowMessage("Не удалось выполнить действие: " + ex.Message, "Ошибка", MessageSeverity.Error);
        }
    }

    private void ImportFiles(IEnumerable<string> paths)
    {
        if (_selectedInstance is null) return;

        var inst = _selectedInstance;
        _instancesService.EnsureFolders(inst);

        var ok = 0;
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var src in paths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var worldDst = IOPath.Combine(_instancesService.SavesDir(inst), IOPath.GetFileName(src));
                    if (Directory.Exists(worldDst)) { skipped.Add(IOPath.GetFileName(src) + " (уже есть)"); continue; }
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
                    dstDir = _instancesService.ModsDir(inst);
                }
                else if (ext == ".zip")
                {
                    dstDir = LooksLikeShaderPack(src)
                        ? _instancesService.ShaderPacksDir(inst)
                        : _instancesService.ResourcePacksDir(inst);
                }

                else
                {
                    skipped.Add(name + " (неизвестный тип)");
                    continue;
                }

                var dst = IOPath.Combine(dstDir, name);
                if (File.Exists(dst)) { skipped.Add(name + " (уже есть)"); continue; }

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

        AppendLog("Импорт: " + report.Replace("\n", "; "));
        RefreshContent();

        _dialog.ShowMessage(report, "Импорт файлов",
            failed.Count > 0 ? MessageSeverity.Warning : MessageSeverity.Info);
    }

    private static bool LooksLikeShaderPack(string zipPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
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

    private void Content_DragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles && _selectedInstance is not null ? DragDropEffects.Copy : DragDropEffects.None;

        if (hasFiles && _selectedInstance is not null)
        {
            DropHint.Visibility = Visibility.Visible;
            TxtDropTarget.Text = $"в сборку «{_selectedInstance.Name}»  ·  .jar → моды, .zip → ресурспаки или шейдеры";
        }

        e.Handled = true;
    }

    private void Content_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (_selectedInstance is null)
        {
            _dialog.ShowMessage("Сначала выберите сборку.", "Сборка не выбрана");
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) ImportFiles(files);
        e.Handled = true;
    }

    public static string? ExtractModrinthSlug(string input)
    {
        input = input.Trim();

        if (input.Contains("modrinth.com", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                input, @"modrinth\.com/(?:mod|plugin|datapack|resourcepack|shader|modpack)/([A-Za-z0-9._-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : null;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Za-z0-9._-]{2,64}$")
            ? input : null;
    }
}

public class ContentTileItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = "";
    private BitmapImage? _iconImage;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _description = "";
    private string _author = "";
    private BitmapImage? _bannerImage;

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string Author
    {
        get => _author;
        set { _author = value; OnPropertyChanged(); }
    }

    public string Category { get; set; } = "";
    public string Info { get; set; } = "";
    public string VersionTag { get; set; } = "";
    public string SizeTag { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsRequired { get; set; }
    public bool IsServerItem { get; set; }
    public string? ModrinthSlug { get; set; }
    public string ToggleText { get; set; } = "";
    public string IconLetter { get; set; } = "V";

    public BitmapImage? IconImage
    {
        get => _iconImage;
        set
        {
            _iconImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultIconVisibility));
        }
    }

    public BitmapImage? BannerImage
    {
        get => _bannerImage;
        set
        {
            _bannerImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultBannerVisibility));
        }
    }

    public Visibility DefaultIconVisibility => IconImage is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DefaultBannerVisibility => BannerImage is null ? Visibility.Visible : Visibility.Collapsed;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
