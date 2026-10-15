using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace VECLauncher.Services;

public static class ProtocolService
{
    public const string CustomScheme = "veclauncher";
    private const string PipeName = "VECLauncher_Ipc_Pipe";
    private static Mutex? _instanceMutex;
    private static CancellationTokenSource? _pipeCts;

    public static void RegisterProtocol()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName 
                              ?? Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return;

            using var schemeKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{CustomScheme}");
            if (schemeKey != null)
            {
                schemeKey.SetValue("", "URL:VEC Launcher Protocol");
                schemeKey.SetValue("URL Protocol", "");

                using var defaultIcon = schemeKey.CreateSubKey("DefaultIcon");
                defaultIcon?.SetValue("", $"\"{exePath}\",0");

                using var commandKey = schemeKey.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to register veclauncher protocol: " + ex.Message);
        }
    }

    public static bool TryEnsureSingleInstance(string[] args)
    {
        try
        {
            _instanceMutex = new Mutex(true, "VECLauncher_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                string message = args.Length > 0 ? string.Join(" ", args) : $"{CustomScheme}://focus";
                if (NotifyExistingInstance(message))
                {
                    return false;
                }
            }

            StartIpcListener();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Single instance check error: " + ex.Message);
            return true;
        }
    }

    private static bool NotifyExistingInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(300);
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void StartIpcListener()
    {
        _pipeCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var token = _pipeCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
#if NET8_0_OR_GREATER
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
#else
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances);
#endif

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? command = await reader.ReadToEndAsync();

                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        Log.Info($"Received IPC command: {command}");
                        BringMainWindowToForeground();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warn("IPC pipe error: " + ex.Message);
                    await Task.Delay(500, token);
                }
            }
        });
    }

    public static void BringMainWindowToForeground()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var win = Application.Current.MainWindow;
                if (win != null)
                {
                    if (win.WindowState == WindowState.Minimized)
                        win.WindowState = WindowState.Normal;

                    win.Show();
                    win.Activate();
                    win.Focus();

                    win.Topmost = true;
                    win.Topmost = false;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to bring window to foreground: " + ex.Message);
            }
        });
    }

    public static void Dispose()
    {
        try
        {
            _pipeCts?.Cancel();
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch { }
    }
}
