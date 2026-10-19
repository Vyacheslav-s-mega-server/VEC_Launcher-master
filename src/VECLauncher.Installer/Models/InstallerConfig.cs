using System;
using System.IO;

namespace VECLauncher.Installer.Models;

public sealed class InstallerConfig
{
    public static string DefaultInstallPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "VECLauncher");

    public string InstallPath { get; set; } = DefaultInstallPath;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;
    public bool RegisterProtocol { get; set; } = true;

    public bool IsSilent { get; set; }
    public bool IsUpdate { get; set; }
    public bool IsUninstall { get; set; }

    public static InstallerConfig FromArgs(string[] args)
    {
        var config = new InstallerConfig();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim().ToLowerInvariant();
            if (arg is "--silent" or "-s" or "/s")
            {
                config.IsSilent = true;
            }
            else if (arg is "--update" or "-u" or "/u")
            {
                config.IsUpdate = true;
            }
            else if (arg is "--uninstall")
            {
                config.IsUninstall = true;
            }
            else if (arg is "--no-desktop-shortcut")
            {
                config.CreateDesktopShortcut = false;
            }
            else if (arg is "--no-start-shortcut")
            {
                config.CreateStartMenuShortcut = false;
            }
            else if (arg is "--no-launch")
            {
                config.LaunchAfterInstall = false;
            }
            else if (arg.StartsWith("--dir=") && arg.Length > 6)
            {
                config.InstallPath = args[i].Trim()[6..].Trim('"', '\'');
            }
            else if (arg == "--dir" && i + 1 < args.Length)
            {
                config.InstallPath = args[++i].Trim('"', '\'');
            }
        }
        return config;
    }
}
