using VECLauncher.Models;

namespace VECLauncher.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string yesText = "Да", string noText = "Отмена");
    Task<ConfirmResult> ConfirmCancelAsync(string title, string message, string yesText = "Да", string noText = "Нет", string cancelText = "Отмена");
    void ShowMessage(string message, string title = "VEC Launcher", MessageSeverity severity = MessageSeverity.Info);
    void ShowToast(string title, string message, ToastType type = ToastType.Info);
    string? BrowseFolder(string description = "");
    string? BrowseFile(string filter, string title = "Выберите файл");
    Task<MinecraftAccount?> ShowVecLoginAsync();
    void MinimizeWindow();
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public enum ConfirmResult
{
    Yes,
    No,
    Cancel
}

public enum MessageSeverity
{
    Info,
    Warning,
    Error
}
