using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public record PreloadStep(double Percent, string Status, string Tip);

public sealed class PreloadResult
{
    public List<JavaInstallation> JavaInstallations { get; init; } = new();
    public VersionManifest? VersionManifest { get; init; }
    public List<BuildManifestFile> ServerManifest { get; init; } = new();
    public MinecraftAccount? ActiveAccount { get; init; }
    public List<GameInstance> Instances { get; init; } = new();
}

public interface IPreloadService
{
    Task<PreloadResult> ExecutePipelineAsync(IProgress<PreloadStep> progress, CancellationToken ct = default);
}

public sealed class PreloadService : IPreloadService
{
    private readonly HttpClient _http;
    private readonly JavaService _javaService;
    private readonly VersionService _versionService;
    private readonly IAccountRepository _accountRepo;
    private readonly IInstanceService _instanceService;
    private readonly IImageCacheService _imageCache;

    public PreloadService(
        HttpClient http,
        JavaService javaService,
        VersionService versionService,
        IAccountRepository accountRepo,
        IInstanceService instanceService,
        IImageCacheService imageCache)
    {
        _http = http;
        _javaService = javaService;
        _versionService = versionService;
        _accountRepo = accountRepo;
        _instanceService = instanceService;
        _imageCache = imageCache;
    }

    public async Task<PreloadResult> ExecutePipelineAsync(IProgress<PreloadStep> progress, CancellationToken ct = default)
    {
        progress.Report(new PreloadStep(10, "Инициализация подсистем VEC...", "Загрузка конфигурации окружения..."));
        await Task.Yield();

        progress.Report(new PreloadStep(25, "Сканирование Java окружения...", "Обнаружение установленных JVM..."));
        var javaList = await Task.Run(() =>
        {
            try { return _javaService.FindAll(); }
            catch (Exception ex)
            {
                Log.Warn("Preload Java scan: " + ex.Message);
                return new List<JavaInstallation>();
            }
        }, ct);

        progress.Report(new PreloadStep(45, "Синхронизация профилей...", "Загрузка активного аккаунта..."));
        var activeAccount = await Task.Run(() =>
        {
            try { return _accountRepo.GetActive(); }
            catch (Exception ex)
            {
                Log.Warn("Preload active account: " + ex.Message);
                return null;
            }
        }, ct);

        progress.Report(new PreloadStep(65, "Проверка версий Minecraft...", "Синхронизация манифеста Mojang..."));
        VersionManifest? versions = null;
        try
        {
            versions = await _versionService.GetManifestAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Warn("Preload Mojang manifest: " + ex.Message);
        }

        progress.Report(new PreloadStep(85, "Синхронизация сборки VEC...", "Получение манифеста сборки сервера..."));
        var serverManifest = new List<BuildManifestFile>();
        try
        {
            var candidates = new List<string> { VecAuthService.DefaultVecServerUrl, LauncherConfig.DevelopmentWebServerUrl, LauncherConfig.ProductionWebServerUrl };
            if (!string.IsNullOrWhiteSpace(activeAccount?.ServerUrl))
                candidates.Insert(0, activeAccount.ServerUrl.TrimEnd('/'));

            foreach (var serverUrl in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                    var json = await _http.GetStringAsync($"{serverUrl}/api/builds/vec-fabric-1-21-11/manifest", timeoutCts.Token);
                    var res = JsonSerializer.Deserialize<BuildManifestResponse>(json);
                    if (res?.Files != null && res.Files.Count > 0)
                    {
                        serverManifest = res.Files;
                        break;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Preload VEC build manifest: " + ex.Message);
        }

        if (serverManifest.Count > 0)
        {
            progress.Report(new PreloadStep(90, "Кэширование медиа-ресурсов...", "Предзагрузка иконок и баннеров..."));
            var mediaUrls = serverManifest
                .SelectMany(f => new[] { f.IconUrl, f.BannerUrl })
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .Take(20)
                .ToList();

            foreach (var url in mediaUrls)
            {
                try
                {
                    await _imageCache.GetAsync(url, _http, 128, ct);
                }
                catch { }
            }
        }

        progress.Report(new PreloadStep(96, "Подготовка игровых инстансов...", "Загрузка установленных сборок..."));
        var instances = await Task.Run(() =>
        {
            try { return _instanceService.LoadAll(); }
            catch (Exception ex)
            {
                Log.Warn("Preload instances: " + ex.Message);
                return new List<GameInstance>();
            }
        }, ct);

        progress.Report(new PreloadStep(100, "Добро пожаловать в VEC!", "Открытие интерфейса..."));

        return new PreloadResult
        {
            JavaInstallations = javaList,
            ActiveAccount = activeAccount,
            VersionManifest = versions,
            ServerManifest = serverManifest,
            Instances = instances
        };
    }
}
