namespace VECLauncher.Services;

public interface IOfflineSkinService
{
    string AccountSkinPath(string username);
    string AccountCapePath(string username);
    string? FindAccountSkin(string username);
    string? FindAccountCape(string username);
}

public sealed class OfflineSkinServiceAdapter : IOfflineSkinService
{
    public string AccountSkinPath(string username) => OfflineSkinService.AccountSkinPath(username);
    public string AccountCapePath(string username) => OfflineSkinService.AccountCapePath(username);
    public string? FindAccountSkin(string username) => OfflineSkinService.FindAccountSkin(username);
    public string? FindAccountCape(string username) => OfflineSkinService.FindAccountCape(username);
}
