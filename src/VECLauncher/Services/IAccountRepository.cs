using VECLauncher.Models;

namespace VECLauncher.Services;

public interface IAccountRepository
{
    MinecraftAccount? GetActive();
    IReadOnlyList<MinecraftAccount> GetAllSaved();
    void Save(MinecraftAccount account);
    void Remove(string username, AccountType type);
    void ClearActiveSession();
}

public sealed class AccountRepository : IAccountRepository
{
    public MinecraftAccount? GetActive() => AccountStorage.Load();
    public IReadOnlyList<MinecraftAccount> GetAllSaved() => AccountStorage.GetAllSaved();
    public void Save(MinecraftAccount account) => AccountStorage.Save(account);
    public void Remove(string username, AccountType type) => AccountStorage.RemoveSaved(username, type);
    public void ClearActiveSession() => AccountStorage.Clear();
}
