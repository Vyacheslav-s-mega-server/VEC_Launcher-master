using System;
using System.IO;
using System.Linq;

namespace VECLauncher.Services;

public static class OfflineSkinService
{
    public static string AccountSkinPath(string username) =>
        Path.Combine(LauncherPaths.Root, "skins", username + ".png");

    public static string AccountCapePath(string username) =>
        Path.Combine(LauncherPaths.Root, "capes", username + ".png");

    public static string? FindAccountSkin(string username)
    {
        var exact = AccountSkinPath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "skins");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindAccountCape(string username)
    {
        var exact = AccountCapePath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "capes");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }
}