namespace VECLauncher.Services;

public sealed class ToastService : IToastService
{
    public void Show(string title, string message, NotificationType type = NotificationType.Info)
    {
        ToastNotification.Show(title, message, type);
    }
}
