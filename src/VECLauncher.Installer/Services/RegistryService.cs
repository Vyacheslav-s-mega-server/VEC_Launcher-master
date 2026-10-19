using System;
using Microsoft.Win32;

namespace VECLauncher.Installer.Services;

public interface IRegistryService
{
    void RegisterProtocol(string targetExePath);
    void UnregisterProtocol();
    void RegisterUninstall(string installDir, string targetExePath, string setupExePath, string version);
    void UnregisterUninstall();
}

public sealed class RegistryService : IRegistryService
{
    private const string ProtocolKeyPath = @"Software\Classes\vec";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VECLauncher";

    public void RegisterProtocol(string targetExePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ProtocolKeyPath);
            if (key == null) return;

            key.SetValue("", "URL:VEC Protocol");
            key.SetValue("URL Protocol", "");

            using var defaultIconKey = key.CreateSubKey("DefaultIcon");
            defaultIconKey?.SetValue("", $"\"{targetExePath}\",0");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue("", $"\"{targetExePath}\" \"%1\"");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register protocol: {ex.Message}");
        }
    }

    public void UnregisterProtocol()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ProtocolKeyPath, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to unregister protocol: {ex.Message}");
        }
    }

    public void RegisterUninstall(string installDir, string targetExePath, string setupExePath, string version)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            if (key == null) return;

            key.SetValue("DisplayName", "VEC Launcher");
            key.SetValue("DisplayVersion", version);
            key.SetValue("DisplayIcon", $"\"{targetExePath}\",0");
            key.SetValue("Publisher", "VEC");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", $"\"{setupExePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 120_000, RegistryValueKind.DWord); // ~120 MB
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register uninstall entry: {ex.Message}");
        }
    }

    public void UnregisterUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to unregister uninstall entry: {ex.Message}");
        }
    }
}
