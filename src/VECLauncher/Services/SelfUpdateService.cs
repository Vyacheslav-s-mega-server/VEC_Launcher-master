using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

using System.Net.Http;
using System.Text.Json;

namespace VECLauncher.Services;

public sealed class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Changelog { get; set; } = "";
}

public static class SelfUpdateService
{
    private const string ProcessName = "VECLauncher";

    public const string ExeName = "VECLauncher.exe";

    public static string DesktopExePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ExeName);

    private static string CurrentExePath => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, ExeName);

    public static async Task<UpdateInfo?> CheckForUpdatesAsync(string? serverUrl = null)
    {
        try
        {
            var srv = !string.IsNullOrEmpty(serverUrl) ? serverUrl.TrimEnd('/') : await VecAuthService.GetActiveServerUrlAsync();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var json = await http.GetStringAsync($"{srv}/api/launcher/version");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("version", out var vProp))
            {
                var serverVerStr = vProp.GetString() ?? "1.0.0";
                if (Version.TryParse(serverVerStr, out var serverVer))
                {
                    var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
                    if (serverVer > currentVer)
                    {
                        return new UpdateInfo
                        {
                            HasUpdate = true,
                            LatestVersion = serverVerStr,
                            DownloadUrl = root.TryGetProperty("downloadUrl", out var d) ? d.GetString() ?? "" : "",
                            Changelog = root.TryGetProperty("changelog", out var c) ? c.GetString() ?? "" : ""
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"CheckForUpdates error: {ex.Message}");
        }
        return null;
    }

    public static bool RunSelfReplacementIfNeeded()
    {
        return false;
    }

    private static void KillOldInstances()
    {
        var self = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            if (p.Id == self) continue;
            try
            {
                p.Kill();
                p.WaitForExit(8000);
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
            finally { try { p.Dispose(); } catch (Exception ex) { Log.Warn(ex.Message); } }
        }
    }

    private static void ScheduleSelfDelete(string path)
    {
        try
        {
            var script = Path.Combine(Path.GetTempPath(), "mays_del_" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "del /f /q \"" + path + "\" >nul 2>&1\r\n" +
                "del /f /q \"" + script + "\" >nul 2>&1\r\n");

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" /min \"" + script + "\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private static Version GetVersion(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Version ?? new Version(0, 0, 0, 0);
        }
        catch
        {
            return new Version(0, 0, 0, 0);
        }
    }

    private static string GetDownloadsDir()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
            var value = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var en = Path.Combine(profile, "Downloads");
        if (Directory.Exists(en)) return en;
        return Path.Combine(profile, "Загрузки");
    }

    private static bool IsUnderDownloads(string dir)
    {
        try
        {
            var downloads = new DirectoryInfo(GetDownloadsDir());
            if (!downloads.Exists) return false;

            var current = new DirectoryInfo(dir ?? string.Empty);
            while (current != null)
            {
                if (string.Equals(current.FullName.TrimEnd('\\'),
                        downloads.FullName.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return true;
                current = current.Parent;
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
        return false;
    }
}