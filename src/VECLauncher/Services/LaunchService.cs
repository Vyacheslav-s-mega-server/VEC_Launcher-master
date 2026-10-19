using System.Diagnostics;
using System.IO;
using VECLauncher.Models;
using VECLauncher.Services;
using IOPath = System.IO.Path;

namespace VECLauncher.Services;

public sealed class LaunchService
{
    private readonly VersionService _versions;
    private readonly DownloadManager _downloads;
    private readonly JavaService _java;
    private readonly SkinService _skins;
    private readonly GameLauncher _game;
    private readonly ModLoaderService _loaders;

    public LaunchService(VersionService versions, DownloadManager downloads,
        JavaService java, SkinService skins,
        GameLauncher game, ModLoaderService loaders)
    {
        _versions = versions;
        _downloads = downloads;
        _java = java;
        _skins = skins;
        _game = game;
        _loaders = loaders;
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? LogLine;

    private void SetStatus(string s) => StatusChanged?.Invoke(s);
    private void Log(string line) => LogLine?.Invoke(line);

    public async Task<LaunchResult> LaunchAsync(
        MinecraftAccount account,
        GameInstance inst,
        LauncherSettings settings,
        VersionManifest? manifest,
        List<GameInstance> instances,
        string? serverAddress,
        bool allowMultipleInstances,
        bool anyRunning,
        bool thisInstanceRunning,
        GameSessionManager sessions,
        CancellationToken ct)
    {
        var paths = GamePaths.ForInstance(inst);
        paths.EnsureAll();

        _versions.Paths = paths;
        _downloads.Paths = paths;
        _loaders.Paths = paths;
        _loaders.InstallRoot = paths.IsIsolated
            ? IOPath.Combine(InstanceService.InstanceDir(inst), ".minecraft")
            : LauncherPaths.Root;

        if (paths.IsIsolated) Log($"Сборка «{inst.Name}» изолирована: файлы в её папке.");

        SetStatus($"Читаю описание версии {inst.McVersion}...");
        var mvManifest = manifest ?? await _versions.GetManifestAsync(ct);
        var mv = mvManifest.Versions.FirstOrDefault(v => v.Id == inst.McVersion)
                 ?? throw new InvalidOperationException($"Версия {inst.McVersion} не найдена в манифесте.");
        var baseDetail = await _versions.GetVersionDetailAsync(mv, ct);

        var requiredJava = baseDetail.JavaVersion?.MajorVersion ?? JavaService.RequiredJavaFor(inst.McVersion);
        SetStatus($"Проверяю Java {requiredJava}...");

        JavaInstallation java;
        var javaOverride = !string.IsNullOrWhiteSpace(inst.JavaPath) ? inst.JavaPath : settings.CustomJavaPath;

        if (!string.IsNullOrWhiteSpace(javaOverride) && File.Exists(javaOverride))
        {
            java = JavaService.Probe(javaOverride, "custom")
                   ?? throw new InvalidOperationException("Указанный java.exe не отвечает.");
            if (java.MajorVersion < requiredJava)
                Log($"ВНИМАНИЕ: выбрана Java {java.MajorVersion}, нужна {requiredJava}.");
        }
        else
        {
            java = await _java.EnsureJavaAsync(requiredJava, ct);
        }

        await Task.Run(() => _downloads.InstallVersionAsync(baseDetail, ct), ct);

        var launchId = inst.EffectiveVersionId;

        if (inst.Loader != LoaderKind.Vanilla)
        {
            var alreadyInstalled = !string.IsNullOrEmpty(inst.LaunchVersionId) &&
                                   File.Exists(paths.VersionJson(inst.LaunchVersionId!));

            if (!alreadyInstalled)
            {
                var loaderVer = inst.LoaderVersion;
                if (string.IsNullOrWhiteSpace(loaderVer) && inst.Loader == LoaderKind.Fabric)
                {
                    loaderVer = await _loaders.GetLatestFabricLoaderVersionAsync(inst.McVersion, ct);
                    inst.LoaderVersion = loaderVer;
                    InstanceService.SaveAll(instances);
                }

                SetStatus($"Устанавливаю {inst.Loader.Display()} {loaderVer}...");
                launchId = await _loaders.InstallAsync(
                    inst.Loader, inst.McVersion, loaderVer, java, ct);

                inst.LaunchVersionId = launchId;
                InstanceService.SaveAll(instances);
            }
            else
            {
                launchId = inst.LaunchVersionId!;
            }
        }

        SetStatus("Готовлю файлы запуска...");
        var finalDetail = await _versions.ResolveAsync(launchId, ct);
        var install = await Task.Run(() => _downloads.InstallVersionAsync(finalDetail, ct), ct);

        bool useAuthlibInjector = false;
        string? authlibServerUrl = null;

        if (account.IsVec)
        {
            var vecResult = await PrepareVecAuthAsync(account, inst, ct);
            useAuthlibInjector = vecResult.UseAuthlib;
            authlibServerUrl = vecResult.ServerUrl;
        }
        if (settings.AutoSetGameLanguage)
        {
            var updated = GameOptionsService.SetLanguage(
                InstanceService.InstanceDir(inst), inst.McVersion, settings.GameLanguage);
            if (updated)
                Log($"Язык игры установлен: " +
                    GameOptionsService.LanguageCodeFor(inst.McVersion, settings.GameLanguage));
        }

        string? launchTicket = null;
        if (account.IsVec)
        {
            var srvUrl = !string.IsNullOrWhiteSpace(authlibServerUrl) ? authlibServerUrl : VecAuthService.DefaultVecServerUrl;
            var integrity = await IntegrityService.ValidateAndSyncAsync(inst, account, srvUrl, Log, SetStatus, ct);
            launchTicket = integrity.Ticket;
        }

        var jvmArgs = string.IsNullOrWhiteSpace(inst.ExtraJvmArgs) ? settings.ExtraJvmArgs : inst.ExtraJvmArgs;

        if (!string.IsNullOrWhiteSpace(launchTicket))
        {
            jvmArgs = $"{jvmArgs} -Dvec.ticket={launchTicket}".Trim();
        }

        var options = new LaunchOptions
        {
            Account = account,
            Install = install,
            Java = java,
            GameDir = InstanceService.InstanceDir(inst),
            MinMemoryMb = Math.Min(1024, EffectiveMemory(inst, settings)),
            MaxMemoryMb = EffectiveMemory(inst, settings),
            WindowWidth = inst.WindowWidth > 0 ? inst.WindowWidth : settings.WindowWidth,
            WindowHeight = inst.WindowHeight > 0 ? inst.WindowHeight : settings.WindowHeight,
            Fullscreen = settings.Fullscreen,
            ServerAddress = serverAddress ?? (string.IsNullOrWhiteSpace(inst.ServerAddress) ? null : inst.ServerAddress),
            ExtraJvmArgs = jvmArgs,
            ShowConsole = settings.ShowConsole,
            CloseLauncherOnStart = settings.CloseLauncherOnStart,
            UseAuthlibInjector = useAuthlibInjector,
            AuthlibServerUrl = authlibServerUrl
        };

        var proc = _game.Launch(options);

        inst.LastPlayed = DateTimeOffset.Now;
        InstanceService.SaveAll(instances);

        return new LaunchResult
        {
            Process = proc,
            LaunchId = launchId,
            InstanceId = inst.Id,
            InstanceName = inst.Name,
            ServerAddress = serverAddress,
            JavaMajorVersion = java.MajorVersion
        };
    }

    private async Task<(bool UseAuthlib, string? ServerUrl)> PrepareVecAuthAsync(
        MinecraftAccount account, GameInstance inst, CancellationToken ct)
    {
        var srvUrl = !string.IsNullOrEmpty(account.ServerUrl) && account.ServerUrl != VecAuthService.DefaultVecServerUrl
            ? account.ServerUrl
            : await VecAuthService.GetActiveServerUrlAsync();

        var serverOnline = await AuthlibInjectorService.IsServerAvailableAsync(srvUrl, ct);
        if (!serverOnline)
        {
            Log($"Сервер VEC ({srvUrl}) пока оффлайн — запуск в автономном режиме со скином из кэша.");
            return (false, null);
        }

        SetStatus("Подготовка VEC ID (authlib-injector)...");
        var jar = await AuthlibInjectorService.EnsureInstalledAsync(ct);
        if (jar == null) return (false, null);

        Log($"Подключен VEC Auth Server ({srvUrl}).");
        return (true, srvUrl);
    }

    private static int EffectiveMemory(GameInstance inst, LauncherSettings settings) =>
        inst.MaxMemoryMb > 0 ? inst.MaxMemoryMb : settings.MaxMemoryMb;
}

public sealed class LaunchResult
{
    public required Process Process { get; init; }
    public required string LaunchId { get; init; }
    public required string InstanceId { get; init; }
    public required string InstanceName { get; init; }
    public string? ServerAddress { get; init; }
    public int JavaMajorVersion { get; init; }
}
