using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private void ApplyWindowBackground()
    {
        var brush = ThemeService.BuildWindowBackground(
            _settings.WindowBackgroundPath, _settings.WindowBackgroundOpacity);

        WindowBgLayer.Fill = brush ?? (Brush)new SolidColorBrush(Colors.Transparent);
    }

    private void PersistSettings()
    {
        _settings.LastInstanceId = _selectedInstance?.Id ?? _settings.LastInstanceId;
        Vm.SaveSettings();
        if (!_initializing && _instancesService.Loaded) _instancesService.SaveAll(_instances);
    }


    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || PageHome is null) return;

        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "0";

        Vm.NavigateCommand.Execute(tag);

        AnimatePageTransition(tag);

        if (tag == "0" || tag == "3")
        {
            ReloadSkin3DModel();
            if (tag == "3" && _account != null && Vm.AvailableCapes.Count == 0)
            {
                _ = RefreshCapesGridUIAsync(_account);
            }
        }
        if (tag == "7") RefreshContent();
    }

    private void AnimatePageTransition(string tag)
    {
        var pages = new Dictionary<string, Grid>
        {
            ["0"] = PageHome,
            ["3"] = PageAccount,
            ["4"] = PageSettings,
            ["7"] = PageContent
        };

        foreach (var kvp in pages)
        {
            kvp.Value.Visibility = kvp.Key == tag ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BlockingControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement element) return;

        if (sender is ComboBox { IsDropDownOpen: true }) return;

        e.Handled = true;

        var parent = FindParentScrollViewer(element);
        parent?.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);

        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SetupWheelHandling(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is ComboBox or Slider)
            {
                var el = (UIElement)child;
                el.PreviewMouseWheel -= BlockingControl_PreviewMouseWheel;
                el.PreviewMouseWheel += BlockingControl_PreviewMouseWheel;
            }

            if (child is not ComboBox) SetupWheelHandling(child);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        var typing = Keyboard.FocusedElement is TextBox or ComboBox;

        switch (e.Key)
        {
            case Key.F5 when !typing || ctrl:
                RefreshCurrentPage();
                e.Handled = true;
                break;

            case Key.F when ctrl:
                NavContent.IsChecked = true;
                e.Handled = true;
                break;

            case Key.F11:
                if (_selectedInstance is not null && !_busy) _ = Vm.LaunchGameCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case Key.Escape when typing:
                Keyboard.ClearFocus();
                e.Handled = true;
                break;

            case >= Key.D1 and <= Key.D9 when ctrl:
                SwitchTab(e.Key - Key.D1);
                e.Handled = true;
                break;
        }
    }

    private void SwitchTab(int index)
    {
        var navs = new[] { NavHome, NavContent, NavAccount, NavSettings };
        if (index >= 0 && index < navs.Length) navs[index].IsChecked = true;
    }

    private void RefreshCurrentPage()
    {
        if (PageContent.Visibility == Visibility.Visible)
        {
            RefreshContent();
        }
        else if (PageSettings.Visibility == Visibility.Visible)
        {
            if (SecPanelStorage.Visibility == Visibility.Visible) Vm.ScanMaintenanceCommand.Execute(null);
            else _ = Vm.DetectJavaCommand.ExecuteAsync(null);
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private bool _isMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMaximized)
        {
            Left = _restoreLeft;
            Top = _restoreTop;
            Width = _restoreWidth;
            Height = _restoreHeight;
            WindowState = WindowState.Normal;
            WindowBorder.CornerRadius = new CornerRadius(0);
            _isMaximized = false;
            MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z");
        }
        else
        {
            _restoreLeft = Left;
            _restoreTop = Top;
            _restoreWidth = Width;
            _restoreHeight = Height;
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            WindowBorder.Margin = new Thickness(0);
            WindowBorder.CornerRadius = new CornerRadius(0);
            _isMaximized = true;
            MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M 2,0 L 10,0 L 10,8 M 0,2 L 0,10 L 8,10");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void WindowBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        WindowBorder.Clip = null;
    }

    private void OnProgress(DownloadProgress p)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUi).TotalMilliseconds < 50 && p.Percent < 100) return;
        _lastProgressUi = now;

        Dispatcher.BeginInvoke(() =>
        {
            Vm.ReportProgress(p);
        });
    }

    private void SetStage(string stage) => Vm.SetStage(stage);

    private void ShowProgress(bool indeterminate = false)
    {
        Vm.ProgressVisible = true;
        Vm.ProgressIndeterminate = indeterminate;
    }

    private void HideProgress() => Vm.HideProgress();

    private void SetBusy(bool busy)
    {
        Vm.SetBusy(busy);
        Dispatcher.Invoke(() =>
        {
            UpdateRunStateUi();
        });
    }

    private void AppendLog(string line) => Vm.AppendLog(line);

    private static string Human(long bytes) => MainWindowViewModel.Human(bytes);
}
