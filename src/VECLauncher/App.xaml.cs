using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.Services;

namespace VECLauncher;

#if DEBUG
internal sealed class BindingErrorListener : TraceListener
{
    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Console.WriteLine("[BINDING] " + message);
        Log.Warn("Binding error: " + message);
    }
}
#endif

public partial class App : Application
{
    public static HttpClient Http { get; private set; } = null!;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (SelfUpdateService.RunSelfReplacementIfNeeded())
        {
            Shutdown();
            return;
        }

        if (!ProtocolService.TryEnsureSingleInstance(e.Args))
        {
            Shutdown();
            return;
        }

        ProtocolService.RegisterProtocol();

        ServicePointManager.DefaultConnectionLimit = 64;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        };

        Http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("VEC Launcher/1.0 (+windows)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("*/*");

#if DEBUG
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
#endif

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled domain exception: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unhandled task exception: " + args.Exception);
            args.SetObserved();
        };

        LauncherPaths.EnsureAll();
        Log.Info("VEC Launcher started");

        try
        {
            Services = ServiceConfig.Configure();
            var s = Services.GetRequiredService<LauncherSettings>();
            ThemeService.ApplyTheme(s.Theme);
            ThemeService.ApplyAccent(s.AccentColor);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal startup error: " + ex);
            MessageBox.Show("Fatal startup error: " + ex.Message + "\n" + ex.StackTrace, "VEC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new Views.SplashWindow();
        splash.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI error", e.Exception);
        MessageBox.Show(
            "An error occurred:\n\n" + e.Exception.Message +
            "\n\nDetails logged to:\n" + LauncherPaths.LauncherLogFile,
            "VEC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Shutting down");
        ProtocolService.Dispose();
        Http?.Dispose();
        base.OnExit(e);
    }
}
