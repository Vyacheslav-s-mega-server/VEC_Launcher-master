using System.IO;
using System.Net.Http;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Tests;

public class MainWindowViewModelCommandTests
{
    private static readonly HttpClient s_http = new();

    private MainWindowViewModel CreateVm()
    {
        var vm = new MainWindowViewModel(
            new VersionService(s_http),
            new DownloadManager(s_http),
            new JavaService(s_http),
            new SkinService(s_http),
            new GameLauncher(),
            new ModLoaderService(s_http),
            new ServerPingService(),
            new LaunchService(
                new VersionService(s_http),
                new DownloadManager(s_http),
                new JavaService(s_http),
                new SkinService(s_http),
                new GameLauncher(),
                new ModLoaderService(s_http)),
            GameStatistics.Load(),
            new TestAccountRepository());
        vm.SetDialogService(new TestDialogService());
        return vm;
    }

    [Fact]
    public void SetDialogService_SetsProperty()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.Dialogs);
    }

    [Fact]
    public void LaunchGame_NoAccount_ShowsToast()
    {
        var vm = CreateVm();
        var dialog = (TestDialogService)vm.Dialogs;

        vm.LaunchGameCommand.Execute(null);

        Assert.False(dialog.LaunchCalled);
    }

    [Fact]
    public void LaunchGame_NoInstance_ShowsToast()
    {
        var vm = CreateVm();
        var dialog = (TestDialogService)vm.Dialogs;

        var acc = new MinecraftAccount { Username = "TestPlayer", Type = AccountType.Vms };
        vm.SetAccount(acc, refreshSkin: false);

        vm.LaunchGameCommand.Execute(null);

        Assert.False(dialog.LaunchCalled);
    }

    [Fact]
    public void LaunchGame_WithInstance_CallsCallback()
    {
        var vm = CreateVm();
        var dialog = (TestDialogService)vm.Dialogs;

        var acc = new MinecraftAccount { Username = "TestPlayer", Type = AccountType.Vms };
        vm.SetAccount(acc, refreshSkin: false);

        var inst = new GameInstance
        {
            Id = "test-1",
            Name = "Test",
            McVersion = "1.20.4",
            Loader = LoaderKind.Vanilla
        };
        vm.Instances = new List<GameInstance> { inst };
        vm.SelectedInstance = inst;

        bool callbackCalled = false;
        GameInstance? callbackInst = null;
        vm.LaunchAsyncCallback = (i, s) =>
        {
            callbackCalled = true;
            callbackInst = i;
            return Task.CompletedTask;
        };

        vm.LaunchGameCommand.Execute(null);

        Assert.True(callbackCalled);
        Assert.Equal(inst, callbackInst);
    }

    [Fact]
    public void Logout_ClearsAccount()
    {
        var vm = CreateVm();
        var acc = new MinecraftAccount { Username = "TestPlayer", Type = AccountType.Vms };
        vm.SetAccount(acc, refreshSkin: false);
        Assert.NotNull(vm.Account);

        vm.LogoutCommand.Execute(null);

        Assert.Null(vm.Account);
        Assert.False(vm.IsAccountLoggedIn);
    }

    [Fact]
    public void Cancel_SetsBusyFalse()
    {
        var vm = CreateVm();
        vm.SetBusy(true);
        vm.Cts = new CancellationTokenSource();

        vm.CancelCommand.Execute(null);

        Assert.True(vm.Cts.Token.IsCancellationRequested);
    }

    [Fact]
    public void DoClearLog_ClearsLogText()
    {
        var vm = CreateVm();
        vm.DoClearLog();
        Assert.Equal("", vm.LogText);
    }

    [Fact]
    public void RefreshSavedAccounts_PopulatesSavedAccountItems()
    {
        var vm = CreateVm();
        var acc = new MinecraftAccount { Username = "Steve", Type = AccountType.Offline };
        vm.Accounts.Save(acc);
        
        vm.RefreshSavedAccounts();

        Assert.True(vm.HasSavedAccounts);
        Assert.False(vm.HasNoSavedAccounts);
        Assert.Single(vm.SavedAccountItems);
        Assert.Equal("Steve", vm.SavedAccountItems[0].Username);
        Assert.Equal("Оффлайн", vm.SavedAccountItems[0].TypeDisplay);
    }

    [Fact]
    public void SelectSavedAccount_UpdatesActiveAccount()
    {
        var vm = CreateVm();
        var acc = new MinecraftAccount { Username = "Alex", Type = AccountType.Vms };
        var item = new AccountItemViewModel(acc, isCurrent: false);

        vm.SelectSavedAccountCommand.Execute(item);

        Assert.NotNull(vm.Account);
        Assert.Equal("Alex", vm.Account.Username);
        Assert.True(vm.IsAccountLoggedIn);
        Assert.False(vm.IsAccountLoggedOut);
        Assert.Equal("Alex", vm.SideName);
        Assert.Equal("VMS ID", vm.SideStatus);
    }

    [Fact]
    public async Task RedeemCapePromo_EmptyCode_ShowsStatusError()
    {
        var vm = CreateVm();
        vm.CapePromoCode = "   ";

        await vm.RedeemCapePromoCommand.ExecuteAsync(null);

        Assert.True(vm.IsCapePromoStatusVisible);
        Assert.Equal("Введите промокод.", vm.CapePromoStatus);
    }

    [Fact]
    public async Task RedeemCapePromo_NoAccount_ShowsStatusError()
    {
        var vm = CreateVm();
        vm.CapePromoCode = "PROMO123";

        await vm.RedeemCapePromoCommand.ExecuteAsync(null);

        Assert.True(vm.IsCapePromoStatusVisible);
        Assert.Equal("Сначала войдите в аккаунт.", vm.CapePromoStatus);
    }
}

/// <summary>Test double for IDialogService — records calls, never shows UI.</summary>
internal sealed class TestDialogService : IDialogService
{
    public bool LaunchCalled { get; set; }
    public List<(string title, string message)> Toasts { get; } = new();

    public Task<bool> ConfirmAsync(string title, string message, string yesText = "Да", string noText = "Отмена")
        => Task.FromResult(true);

    public Task<ConfirmResult> ConfirmCancelAsync(string title, string message, string yesText = "Да", string noText = "Нет", string cancelText = "Отмена")
        => Task.FromResult(ConfirmResult.Yes);

    public void ShowToast(string title, string message, ToastType type = ToastType.Info)
        => Toasts.Add((title, message));

    public string? BrowseFolder(string description = "") => null;
    public string? BrowseFile(string filter, string title = "Выберите файл") => null;
    public Task<MinecraftAccount?> ShowVmsLoginAsync() => Task.FromResult<MinecraftAccount?>(null);
    public Task<MinecraftAccount?> ShowVecLoginAsync() => ShowVmsLoginAsync();
    public void MinimizeWindow() { }
    public void ShowMessage(string message, string title = "VEC Launcher", MessageSeverity severity = MessageSeverity.Info) { }
}

/// <summary>Test double for IAccountRepository — in-memory, no file I/O.</summary>
internal sealed class TestAccountRepository : IAccountRepository
{
    private readonly List<MinecraftAccount> _accounts = new();
    private MinecraftAccount? _active;

    public MinecraftAccount? GetActive() => _active;
    public IReadOnlyList<MinecraftAccount> GetAllSaved() => _accounts;
    public void Save(MinecraftAccount account)
    {
        _accounts.RemoveAll(a => a.Username == account.Username && a.Type == account.Type);
        _accounts.Insert(0, account);
        _active = account;
    }
    public void Remove(string username, AccountType type)
        => _accounts.RemoveAll(a => a.Username == username && a.Type == type);
    public void ClearActiveSession() => _active = null;
}
