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
        services.AddSingleton<WebAuthService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ICapeService, CapeService>();
        services.AddSingleton<IContentService, ContentService>();

        services.AddSingleton<IInstanceService, InstanceServiceAdapter>();
        services.AddSingleton<IMaintenanceService, MaintenanceService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<LaunchService>();

        services.AddSingleton<GameStatistics>();

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
