using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class WebAuthService
{
    private const int DefaultPort = 25588;

    public async Task<MinecraftAccount?> LoginViaWebAsync(CancellationToken ct = default)
    {
        int port = GetAvailablePort(DefaultPort);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
        
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Log.Error("WebAuthService listener start failed: " + ex.Message);
            return null;
        }

        string authUrl = $"{VecAuthService.DefaultVecServerUrl}/Account/LauncherAuth?port={port}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("WebAuthService open browser failed: " + ex.Message);
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

            if (completedTask != contextTask)
            {
                listener.Stop();
                return null;
            }

            var context = await contextTask;
            var request = context.Request;
            var response = context.Response;

            string? sessionKey = request.QueryString["sessionKey"];
            string? username = request.QueryString["username"];
            string? skinModel = request.QueryString["skinModel"];
            string? hasLicenseStr = request.QueryString["hasLicense"];
            bool.TryParse(hasLicenseStr, out bool hasLicense);
            string? mcGamertag = request.QueryString["mcGamertag"];
            string? mcUuid = request.QueryString["mcUuid"];

            var srvUrl = VecAuthService.DefaultVecServerUrl;
            string safeUsername = WebUtility.HtmlEncode(username ?? "Игрок");

            string html = $$"""
                <!DOCTYPE html>
                <html lang="ru">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                    <title>VEC Launcher — Авторизация успешна</title>
                    <style>
                        @font-face {
                            font-family: 'minecraft-title';
                            src: url('data:font/truetype;charset=utf-8;base64,{{EmbeddedFonts.MinecraftTitleBase64}}') format('truetype');
                            font-weight: 100 900;
                            font-style: normal;
                        }
                        @font-face {
                            font-family: 'minecraft';
                            src: url('data:font/truetype;charset=utf-8;base64,{{EmbeddedFonts.MinecraftBase64}}') format('truetype');
                            font-weight: 100 900;
                            font-style: normal;
                        }
                        * {
                            box-sizing: border-box;
                            margin: 0;
                            padding: 0;
                            text-shadow: none !important;
                        }
                        body {
                            background-color: #08090d;
                            color: #ffffff;
                            font-family: 'minecraft', sans-serif;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            min-height: 100vh;
                            padding: 20px;
                        }
                        .auth-card {
                            background: #0e1118;
                            border: 1px solid rgba(255, 255, 255, 0.1);
                            border-radius: 10px;
                            padding: 36px 30px;
                            max-width: 440px;
                            width: 100%;
                            text-align: center;
                            box-sizing: border-box;
                        }
                        .auth-title {
                            font-family: 'minecraft-title', 'minecraft', sans-serif !important;
                            font-size: 30px !important;
                            font-weight: 700 !important;
                            line-height: 1.3 !important;
                            letter-spacing: 1.5px !important;
                            color: #ffffff !important;
                            margin: 0 0 16px 0 !important;
                            text-transform: uppercase;
                            text-shadow: none !important;
                        }
                        .auth-subtitle {
                            font-family: 'minecraft', sans-serif !important;
                            font-size: 16px !important;
                            font-weight: normal !important;
                            color: #c9cdd8 !important;
                            margin: 0 0 24px 0 !important;
                            line-height: 1.5 !important;
                        }
                        .auth-username {
                            color: #fbbf24 !important;
                            font-weight: normal !important;
                        }
                        .btn-action {
                            display: block;
                            width: 100%;
                            box-sizing: border-box;
                            padding: 13px 24px;
                            background: #f59e0b;
                            color: #08090d !important;
                            border: 1px solid #fbbf24;
                            border-radius: 8px;
                            font-family: 'minecraft', sans-serif !important;
                            font-size: 16px !important;
                            letter-spacing: 0.8px !important;
                            text-decoration: none;
                            text-align: center;
                            cursor: pointer;
                            transition: background-color 0.15s ease;
                            user-select: none;
                        }
                        .btn-action:hover {
                            background: #d97706;
                        }
                        .auth-hint {
                            font-family: 'minecraft', sans-serif !important;
                            font-size: 13px !important;
                            color: #94a3b8 !important;
                            line-height: 1.5 !important;
                            margin: 20px 0 0 0 !important;
                        }
                        .auth-footer {
                            font-family: 'minecraft', sans-serif !important;
                            font-size: 12px !important;
                            color: #64748b !important;
                            margin: 16px 0 0 0 !important;
                        }
                    </style>
                </head>
                <body>
                    <div class="auth-card">
                        <h1 class="auth-title">АВТОРИЗАЦИЯ УСПЕШНА</h1>
                        <p class="auth-subtitle">Вы вошли как <span class="auth-username">{{safeUsername}}</span>.</p>

                        <a href="veclauncher://auth-success" class="btn-action" id="btnLaunch">
                            ВЕРНУТЬСЯ В ЛАУНЧЕР
                        </a>

                        <p class="auth-hint">
                            В окне браузера нажмите «Открыть приложение» и поставьте галочку «Всегда разрешать».
                        </p>

                        <p class="auth-footer">
                            Вкладку можно закрыть
                        </p>
                    </div>

                    <script>
                        function triggerProtocol() {
                            try {
                                window.location.href = "veclauncher://auth-success";
                            } catch (e) {
                            }
                        }

                        window.addEventListener('load', () => {
                            setTimeout(triggerProtocol, 400);
                        });
                    </script>
                </body>
                </html>
                """;

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            ProtocolService.BringMainWindowToForeground();

            if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var model = string.Equals(skinModel, "slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";

            var acc = new MinecraftAccount
            {
                Type = AccountType.Vec,
                Username = username,
                AccessToken = sessionKey,
                ServerUrl = srvUrl,
                SkinModel = model,
                HasMinecraftLicense = hasLicense,
                MicrosoftGamertag = mcGamertag,
                MicrosoftUuid = mcUuid,
                Uuid = !string.IsNullOrEmpty(mcUuid) ? mcUuid : Guid.NewGuid().ToString("D"),
                SkinUrl = $"{srvUrl}/api/skin/{username}.png",
                CapeUrl = $"{srvUrl}/api/cape/{username}.png"
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var sBytes = await http.GetByteArrayAsync($"{srvUrl}/api/skin/{username}.png?t={DateTime.UtcNow.Ticks}");
                    if (sBytes.Length > 0)
                    {
                        var sPath = OfflineSkinService.AccountSkinPath(username);
                        Directory.CreateDirectory(Path.GetDirectoryName(sPath)!);
                        await File.WriteAllBytesAsync(sPath, sBytes);
                    }
                }
                catch { }

                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var cBytes = await http.GetByteArrayAsync($"{srvUrl}/api/cape/{username}.png?t={DateTime.UtcNow.Ticks}");
                    if (cBytes.Length > 0)
                    {
                        var cPath = OfflineSkinService.AccountCapePath(username);
                        Directory.CreateDirectory(Path.GetDirectoryName(cPath)!);
                        await File.WriteAllBytesAsync(cPath, cBytes);
                    }
                }
                catch { }
            });

            return acc;
        }
        catch (Exception ex)
        {
            Log.Warn("WebAuthService callback error: " + ex.Message);
            return null;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private static int GetAvailablePort(int preferredPort)
    {
        try
        {
            using var tcp = new TcpListener(IPAddress.Loopback, preferredPort);
            tcp.Start();
            tcp.Stop();
            return preferredPort;
        }
        catch
        {
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }
}
