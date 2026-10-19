using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.Installer.Models;
using VECLauncher.Installer.Services;
using VECLauncher.Installer.ViewModels;
using VECLauncher.Installer.Views;

namespace VECLauncher.Installer;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = InstallerConfig.FromArgs(e.Args);

        var services = new ServiceCollection();

        services.AddSingleton(config);
        services.AddSingleton<HttpClient>(sp =>
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "VECLauncher-Installer/1.0");
            return client;
        });

        services.AddSingleton<IShortcutService, ShortcutService>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<InstallerViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        if (config.IsSilent)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var installService = _serviceProvider.GetRequiredService<IInstallService>();
            bool success;

            try
            {
                if (config.IsUninstall)
                {
                    success = await installService.UninstallAsync(config, CancellationToken.None);
                }
                else
                {
                    success = await installService.InstallAsync(config, CancellationToken.None);
                }
            }
            catch
            {
                success = false;
            }

            Shutdown(success ? 0 : 1);
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var viewModel = _serviceProvider.GetRequiredService<InstallerViewModel>();

        mainWindow.DataContext = viewModel;
        viewModel.RequestClose = () => mainWindow.Close();

        mainWindow.Show();
    }
}
