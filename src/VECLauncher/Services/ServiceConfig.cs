using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.ViewModels;

namespace VECLauncher.Services;

public static class ServiceConfig
{
    public static IServiceProvider Configure()
    {
        var services = new ServiceCollection();

        services.AddSingleton(App.Http);

        services.AddSingleton<VersionService>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<JavaService>();
        services.AddSingleton<SkinService>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<ModLoaderService>();
        services.AddSingleton<ServerPingService>();
        services.AddSingleton<ModpackService>();
        services.AddSingleton<WebAuthService>();

        services.AddSingleton<IInstanceService, InstanceServiceAdapter>();
        services.AddSingleton<IOfflineSkinService, OfflineSkinServiceAdapter>();
        services.AddSingleton<IMaintenanceService, MaintenanceServiceAdapter>();
        services.AddSingleton<IImageCacheService, ImageCacheServiceAdapter>();
        services.AddSingleton<LaunchService>();

        services.AddSingleton<GameStatistics>();

        services.AddSingleton<IToastService, ToastService>();

        services.AddSingleton<IAccountRepository, AccountRepository>();

        services.AddSingleton(SettingsService.Load());

        services.AddSingleton<IPreloadService, PreloadService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SplashViewModel>();

        services.AddTransient<Views.SplashWindow>();
        services.AddTransient<Views.MainWindow>();

        return services.BuildServiceProvider();
    }
}
