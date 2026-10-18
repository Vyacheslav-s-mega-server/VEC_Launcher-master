using IOPath = System.IO.Path;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private HomeButtonState _homeBtnState = HomeButtonState.Idle;

    private async void BtnConnectVEC_Click(object sender, RoutedEventArgs e)
    {
        await Vm.LaunchGameCommand.ExecuteAsync(null);
    }

    private void BtnConnectVEC_MouseEnter(object sender, MouseEventArgs e)
    {
        if (BtnConnectVEC?.Template == null) return;
        var bd = BtnConnectVEC.Template.FindName("BtnHomeBd", BtnConnectVEC) as Border;
        if (bd == null) return;

        if (_homeBtnState == HomeButtonState.Idle)
            bd.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        else if (_homeBtnState == HomeButtonState.Busy)
            bd.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        else
            bd.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
    }

    private void BtnConnectVEC_MouseLeave(object sender, MouseEventArgs e)
    {
        if (BtnConnectVEC?.Template == null) return;
        var bd = BtnConnectVEC.Template.FindName("BtnHomeBd", BtnConnectVEC) as Border;
        if (bd == null) return;

        if (_homeBtnState == HomeButtonState.Idle)
            bd.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        else if (_homeBtnState == HomeButtonState.Busy)
            bd.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        else
            bd.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    }

    private async Task LaunchAsync(GameInstance inst, string? serverAddress)
    {
        if (_busy) return;

        if (_account is null)
        {
            _dialog.ShowMessage(
                "Сначала войдите в аккаунт на вкладке «Аккаунт».\n\n" +
                "Требуется авторизация через VEC ID.", "Требуется вход");
            NavAccount.IsChecked = true;
            return;
        }

        _sessions.Prune();
        var runningSessions = _sessions.Sessions.Where(s => s.IsRunning).ToList();
        if (runningSessions.Count > 0)
        {
            var names = string.Join(", ", runningSessions.Select(s => s.InstanceName));
            var isThisRunning = _sessions.IsInstanceRunning(inst.Id);
            var prompt = isThisRunning
                ? $"Сборка «{inst.Name}» уже запущена.\n\nЗапустить ещё одну копию игры?"
                : $"Minecraft уже запущен ({names}).\n\nЗапустить ещё одну копию («{inst.Name}»)?";

            var confirmed = ConfirmDialog.Show(
                this,
                "Minecraft уже запущен",
                prompt,
                confirmText: "Запустить",
                cancelText: "Отмена");

            if (!confirmed)
            {
                return;
            }
        }

        PersistSettings();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);

        try
        {
            var result = await Vm.Launch.LaunchAsync(
                _account, inst, _settings, _manifest, _instances,
                serverAddress, _settings.AllowMultipleInstances,
                runningSessions.Count > 0, _sessions.IsInstanceRunning(inst.Id),
                _sessions, ct);

            _sessions.Register(new GameSession
            {
                Process = result.Process,
                Pid = result.Process.Id,
                VersionId = result.LaunchId,
                InstanceId = result.InstanceId,
                InstanceName = result.InstanceName
            });

            try { _stats.RecordLaunch(inst.Id, inst.Name); } catch (Exception ex) { Log.Warn(ex.Message); }

            AppendLog($"Minecraft запущен (PID {result.Process.Id}), сборка «{result.InstanceName}».");
            if (serverAddress is not null) AppendLog($"Автоподключение к серверу {serverAddress}.");
            SetStage("Игра запущена");

            if (_settings.CloseLauncherOnStart)
            {
                await Task.Delay(2500, ct);
                Application.Current.Shutdown();
                return;
            }

            if (_settings.MinimizeOnLaunch) WindowState = WindowState.Minimized;

            var exitedFast = await Task.Run(() => result.Process.WaitForExit(9000), ct);
            if (exitedFast && result.Process.ExitCode != 0)
            {
                WindowState = WindowState.Normal;
                Activate();
                AppendLog($"Игра завершилась сразу с кодом {result.Process.ExitCode}.");
                _dialog.ShowMessage(
                    $"Minecraft завершился с кодом {result.Process.ExitCode}.\nОткройте «Консоль» для деталей.", "Игра не запустилась", MessageSeverity.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Операция отменена.");
            SetStage("Отменено");
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка запуска", ex);
            _dialog.ShowMessage(ex.Message, "Ошибка запуска", MessageSeverity.Error);
            SetStage("Ошибка");
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            UpdateRunStateUi();
        }
    }




    private void OnSessionExited(GameSession session, int code)
    {
        var seconds = (long)session.Uptime.TotalSeconds;

        try
        {
            _stats.RecordPlayTime(session.InstanceId, (int)seconds);
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        Dispatcher.BeginInvoke(() =>
        {
            var inst = _instances.FirstOrDefault(i => i.Id == session.InstanceId);
            if (inst is not null)
            {
                inst.AddSession(seconds);
                _instancesService.SaveAll(_instances);
            }

            StatTotalTime.Text = _stats.GetFormattedTotalTime();

            AppendLog($"--- «{session.InstanceName}» завершилась (код {code}), " +
                      $"время сессии {session.UptimeDisplay} ---");

            if (WindowState == WindowState.Minimized && !_sessions.AnyRunning)
                WindowState = WindowState.Normal;

            UpdateRunStateUi();
        });
    }

    private void UpdateRunStateUi()
    {
        _sessions.Prune();
        var anyRunning = _sessions.AnyRunning;
        UpdateHomePlayButton(anyRunning);
    }

    private void UpdateHomePlayButton(bool anyRunning)
    {
        if (BtnConnectVEC?.Template == null) return;

        var bd = BtnConnectVEC.Template.FindName("BtnHomeBd", BtnConnectVEC) as Border;
        var txt = BtnConnectVEC.Template.FindName("BtnHomeText", BtnConnectVEC) as TextBlock;
        var shadow = BtnConnectVEC.Template.FindName("BtnHomeShadow", BtnConnectVEC) as DropShadowEffect;
        if (bd == null || txt == null) return;

        if (_busy)
        {
            _homeBtnState = HomeButtonState.Busy;
            txt.Text = "ОТМЕНА";
            txt.Foreground = Brushes.White;
            bd.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            if (shadow != null) { shadow.Color = Colors.Black; shadow.Opacity = 0.3; shadow.BlurRadius = 16; }
            BtnConnectVEC.Cursor = Cursors.Hand;
        }
        else
        {
            _homeBtnState = HomeButtonState.Idle;
            txt.Text = "ИГРАТЬ";
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0D));
            bd.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            bd.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            bd.BorderThickness = new Thickness(1);
            bd.CornerRadius = new CornerRadius(8);
            if (shadow != null) { shadow.Color = Color.FromRgb(0xF5, 0x9E, 0x0B); shadow.Opacity = 0.3; shadow.BlurRadius = 16; }
            BtnConnectVEC.Cursor = Cursors.Hand;
        }
    }



}
