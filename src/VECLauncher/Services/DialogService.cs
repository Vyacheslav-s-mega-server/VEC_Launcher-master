using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VECLauncher.Models;
using VECLauncher.Views;

namespace VECLauncher.Services;

public sealed class DialogService : IDialogService
{
    private readonly Window? _owner;
    private readonly WebAuthService _webAuth;

    public DialogService(WebAuthService webAuth, Window? owner = null)
    {
        _webAuth = webAuth;
        _owner = owner;
    }

    private Window? GetOwner()
    {
        if (_owner != null) return _owner;
        if (Application.Current == null) return null;
        return Application.Current.Dispatcher.CheckAccess()
            ? (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow)
            : Application.Current.Dispatcher.Invoke(() => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow);
    }

    public Task<bool> ConfirmAsync(string title, string message, string yesText = "Да", string noText = "Отмена")
    {
        var owner = GetOwner();
        var result = owner != null
            ? ConfirmDialog.Show(owner, title, message, yesText, noText)
            : ConfirmDialog.Show(title, message, yesText, noText);
        return Task.FromResult(result);
    }

    public void ShowToast(string title, string message, ToastType type = ToastType.Info)
    {
        var nt = type switch
        {
            ToastType.Success => NotificationType.Success,
            ToastType.Warning => NotificationType.Warning,
            ToastType.Error => NotificationType.Error,
            _ => NotificationType.Info
        };
        ToastNotification.Show(title, message, nt);
    }

    public string? BrowseFolder(string description = "")
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = string.IsNullOrEmpty(description) ? "Выберите папку" : description
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public string? BrowseFile(string filter, string title = "Выберите файл")
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public async Task<MinecraftAccount?> ShowVmsLoginAsync()
    {
        return await _webAuth.LoginViaWebAsync();
    }

    public Task<MinecraftAccount?> ShowVecLoginAsync() => ShowVmsLoginAsync();

    public void MinimizeWindow()
    {
        var owner = GetOwner();
        if (owner == null) return;
        if (owner.Dispatcher.CheckAccess())
            owner.WindowState = WindowState.Minimized;
        else
            owner.Dispatcher.Invoke(() => owner.WindowState = WindowState.Minimized);
    }

    public Task<ConfirmResult> ConfirmCancelAsync(string title, string message, string yesText = "Да", string noText = "Нет", string cancelText = "Отмена")
    {
        var owner = GetOwner();
        MessageBoxResult r;
        if (owner != null)
        {
            r = (MessageBoxResult)owner.Dispatcher.Invoke(() =>
                MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question));
        }
        else
        {
            r = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }

        var result = r switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No => ConfirmResult.No,
            _ => ConfirmResult.Cancel
        };
        return Task.FromResult(result);
    }

    public void ShowMessage(string message, string title = "VEC Launcher", MessageSeverity severity = MessageSeverity.Info)
    {
        var icon = severity switch
        {
            MessageSeverity.Warning => MessageBoxImage.Warning,
            MessageSeverity.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };

        var owner = GetOwner();
        if (owner != null)
        {
            owner.Dispatcher.Invoke(() => MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon));
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }
    }
}
