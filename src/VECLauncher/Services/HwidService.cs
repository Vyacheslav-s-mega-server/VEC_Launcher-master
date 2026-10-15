using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace VECLauncher.Services;

public static class HwidService
{
    private static string? _cachedHwid;

    public static string GetHwid()
    {
        if (!string.IsNullOrEmpty(_cachedHwid)) return _cachedHwid;

        try
        {
            var sb = new StringBuilder();
            sb.Append(Environment.MachineName);
            sb.Append(Environment.UserName);
            sb.Append(Environment.OSVersion.VersionString);
            sb.Append(Environment.ProcessorCount);

            try
            {
                var guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString();
                if (!string.IsNullOrEmpty(guid))
                    sb.Append(guid);
            }
            catch { }

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            _cachedHwid = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            _cachedHwid = Guid.NewGuid().ToString("N");
        }

        return _cachedHwid;
    }
}
