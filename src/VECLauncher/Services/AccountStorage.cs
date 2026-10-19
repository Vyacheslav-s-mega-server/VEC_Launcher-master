using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public static class AccountStorage
{
    private static readonly object _lock = new();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VEC.VECLauncher.MultiAcc.v2");
    private static readonly string SavedAccountsFile = Path.Combine(
        LauncherPaths.Root, "saved_accounts.dat");

    public static void Save(MinecraftAccount account)
    {
        lock (_lock)
        {
            try
            {
                LauncherPaths.EnsureAll();
                SaveActive(account);
                AddOrUpdateSaved(account);
            }
            catch (Exception ex)
            {
                Log.Warn($"AccountStorage: failed to persist account '{account?.Username}': {ex.Message}");
            }
        }
    }

    private static void SaveActive(MinecraftAccount account)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(account);
        var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);

        var tmp = LauncherPaths.AccountFile + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);

        if (File.Exists(LauncherPaths.AccountFile))
        {
            try { File.Copy(LauncherPaths.AccountFile, LauncherPaths.AccountFile + ".bak", true); } catch (Exception ex) { Log.Warn(ex.Message); }
            File.Replace(tmp, LauncherPaths.AccountFile, null);
        }
        else File.Move(tmp, LauncherPaths.AccountFile);
    }

    public static MinecraftAccount? Load()
    {
        lock (_lock)
        {
            try
            {
                foreach (var file in new[] { LauncherPaths.AccountFile, LauncherPaths.AccountFile + ".bak" })
                {
                    if (!File.Exists(file)) continue;

                    try
                    {
                        var protectedBytes = File.ReadAllBytes(file);
                        var json = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                        var acc = JsonSerializer.Deserialize<MinecraftAccount>(json);
                        if (acc is null || string.IsNullOrEmpty(acc.Username)) continue;

                        if (acc.IsVec && string.IsNullOrWhiteSpace(acc.ServerUrl))
                        {
                            acc.ServerUrl = VecAuthService.DefaultVecServerUrl;
                        }

                        return acc;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"AccountStorage: couldn't read {Path.GetFileName(file)}, falling through: {ex.Message}");
                    }
                }

                Log.Info("AccountStorage: no active session found, attempting to load first saved account");
                return GetAllSavedUnlocked().FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Warn($"AccountStorage: failed to load active account: {ex.Message}");
                return null;
            }
        }
    }

    public static List<MinecraftAccount> GetAllSaved()
    {
        lock (_lock)
        {
            return GetAllSavedUnlocked();
        }
    }

    private static List<MinecraftAccount> GetAllSavedUnlocked()
    {
        try
        {
            if (!File.Exists(SavedAccountsFile)) 
            {
                Log.Info("AccountStorage: saved accounts file doesn't exist yet");
                return new List<MinecraftAccount>();
            }

            var bytes = File.ReadAllBytes(SavedAccountsFile);
            if (bytes.Length == 0) 
            {
                Log.Warn("AccountStorage: saved accounts file is empty");
                return new List<MinecraftAccount>();
            }

            var json = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<MinecraftAccount>>(json);
            if (list != null)
            {
                foreach (var a in list)
                {
                    if (a.IsVec && string.IsNullOrWhiteSpace(a.ServerUrl))
                    {
                        a.ServerUrl = VecAuthService.DefaultVecServerUrl;
                    }
                }
            }
            
            Log.Info($"AccountStorage: loaded {list?.Count ?? 0} saved accounts");
            return list ?? new List<MinecraftAccount>();
        }
        catch (Exception ex)
        {
            Log.Warn($"AccountStorage: failed to load saved accounts list: {ex.Message}");
            return new List<MinecraftAccount>();
        }
    }

    private static void AddOrUpdateSaved(MinecraftAccount account)
    {
        var all = GetAllSavedUnlocked();
        all.RemoveAll(a => a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase) && a.Type == account.Type);
        all.Insert(0, account);
        SaveAllUnlocked(all);
    }

    public static void RemoveSaved(string username, AccountType type)
    {
        lock (_lock)
        {
            var all = GetAllSavedUnlocked();
            var removed = all.RemoveAll(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && a.Type == type);
            SaveAllUnlocked(all);
            
            if (removed > 0)
                Log.Info($"AccountStorage: removed account '{username}' ({type}) from saved list");
        }
    }

    private static void SaveAllUnlocked(List<MinecraftAccount> accounts)
    {
        try
        {
            var dir = Path.GetDirectoryName(SavedAccountsFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.SerializeToUtf8Bytes(accounts);
            var protectedBytes = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SavedAccountsFile, protectedBytes);
            
            Log.Info($"AccountStorage: saved {accounts.Count} accounts to disk");
        }
        catch (Exception ex)
        {
            Log.Warn($"AccountStorage: failed to save accounts list: {ex.Message}");
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(LauncherPaths.AccountFile))
                {
                    File.Delete(LauncherPaths.AccountFile);
                    Log.Info("AccountStorage: active session cleared");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"AccountStorage: failed to clear active session: {ex.Message}");
            }
        }
    }
}