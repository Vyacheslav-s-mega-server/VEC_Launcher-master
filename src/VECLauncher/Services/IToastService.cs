namespace VECLauncher.Services;

public interface IToastService
{
    void Show(string title, string message, NotificationType type = NotificationType.Info);
}
