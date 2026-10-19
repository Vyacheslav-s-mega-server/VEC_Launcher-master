using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private void OnSessionExited(GameSession session, int code)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (WindowState == WindowState.Minimized && !_sessions.AnyRunning)
                WindowState = WindowState.Normal;

            UpdateRunStateUi();
        });
    }

    private void UpdateRunStateUi()
    {
        _sessions.Prune();
        Vm.UpdateHomePlayButtonState();
    }
}
