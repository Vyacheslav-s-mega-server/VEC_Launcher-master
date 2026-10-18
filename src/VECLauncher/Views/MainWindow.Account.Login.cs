using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{


    private async void BtnVecLogin_Click(object sender, RoutedEventArgs e)
    {
        await Vm.LoginVecCommand.ExecuteAsync(null);
        if (Vm.Account != null)
            SetAccount(Vm.Account, refreshSkin: true);
        RefreshSidebarAccounts();
    }

    private ImageSource? GetAccountAvatarImage(MinecraftAccount acc, int size = 32)
    {
        try
        {
            var localFile = SkinService.FindAccountSkin(acc.Username);
            if (localFile != null && File.Exists(localFile))
            {
                var bytes = File.ReadAllBytes(localFile);
                var head = SkinService.CreateCrispHeadAvatar(bytes, size);
                if (head != null) return head;
            }

            if (_account != null && _account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) && _currentSkinRawBytes != null && _currentSkinRawBytes.Length > 0)
            {
                var head = SkinService.CreateCrispHeadAvatar(_currentSkinRawBytes, size);
                if (head != null) return head;
            }

            if (!string.IsNullOrEmpty(acc.SkinUrl))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                        using var resp = await http.GetAsync(acc.SkinUrl);
                        if (resp.IsSuccessStatusCode)
                        {
                            var downloaded = await resp.Content.ReadAsByteArrayAsync();
                            var path = SkinService.AccountSkinPath(acc.Username);
                            var dir = System.IO.Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            await File.WriteAllBytesAsync(path, downloaded);
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                });
            }


        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return null;
    }

    private void RefreshSidebarAccounts()
    {
        if (StackSidebarAccounts == null) return;

        StackSidebarAccounts.Children.Clear();
        var saved = Vm.Accounts.GetAllSaved();

        if (saved.Count == 0)
        {
            var tbEmpty = new TextBlock
            {
                Text = "Нет сохранённых аккаунтов",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 130)),
                Margin = new Thickness(6, 4, 6, 6)
            };
            StackSidebarAccounts.Children.Add(tbEmpty);
        }
        else
        {
            foreach (var acc in saved)
            {
                var isCurrent = _account != null &&
                                _account.Username.Equals(acc.Username, StringComparison.OrdinalIgnoreCase) &&
                                _account.Type == acc.Type;

                var btn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 2)
                };

                var border = new Border
                {
                    CornerRadius = new CornerRadius(0),
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = isCurrent ? new SolidColorBrush(Color.FromRgb(22, 22, 30)) : Brushes.Transparent
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var imgBox = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(0),
                    Background = new SolidColorBrush(Color.FromRgb(24, 24, 32)),
                    ClipToBounds = true
                };

                var avatarHead = GetAccountAvatarImage(acc, 32);
                if (avatarHead != null)
                {
                    var img = new Image
                    {
                        Source = avatarHead,
                        Stretch = Stretch.UniformToFill
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
                    imgBox.Child = img;
                }
                else
                {
                    var tbIcon = new TextBlock
                    {
                        Text = acc.IsVec ? "V" : (acc.IsOffline ? "O" : "M"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    imgBox.Child = tbIcon;
                }

                Grid.SetColumn(imgBox, 0);
                grid.Children.Add(imgBox);

                var stack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var tbName = new TextBlock
                {
                    Text = acc.Username,
                    FontSize = 11,
                    FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isCurrent ? Brushes.White : new SolidColorBrush(Color.FromRgb(180, 180, 190)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var tbType = new TextBlock
                {
                    Text = acc.IsVec ? "VEC ID" : (acc.IsOffline ? "Оффлайн" : "Microsoft"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 100))
                };
                stack.Children.Add(tbName);
                stack.Children.Add(tbType);
                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);

                if (isCurrent)
                {
                    var tbCheck = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    Grid.SetColumn(tbCheck, 2);
                    grid.Children.Add(tbCheck);
                }

                border.Child = grid;
                btn.Content = border;

                var targetAcc = acc;
                btn.Click += (s, ev) =>
                {
                    PopupSavedAccounts.IsOpen = false;
                    Vm.Accounts.Save(targetAcc);
                    SetAccount(targetAcc, refreshSkin: true);
                    _toast.Show("Профиль переключен", $"Активен аккаунт: {targetAcc.Username}", NotificationType.Info);
                };

                StackSidebarAccounts.Children.Add(btn);
            }
        }

    }

    private void BorderSideUser_Click(object sender, MouseButtonEventArgs e)
    {
        RefreshSidebarAccounts();
        PopupSavedAccounts.IsOpen = true;
    }

    private void BtnSidebarAddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (PopupSavedAccounts != null) PopupSavedAccounts.IsOpen = false;
        BtnVecLogin_Click(sender, e);
    }

    private void BtnSidebarLogout_Click(object sender, RoutedEventArgs e)
    {
        if (PopupSavedAccounts != null) PopupSavedAccounts.IsOpen = false;
        BtnLogout_Click(sender, e);
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        Vm.LogoutCommand.Execute(null);

        _currentSkinRawBytes = null;
        TxtAccName.Text = "—";
        TxtSideName.SetCurrentValue(TextBlock.TextProperty, "Не выполнен вход");
        TxtSideStatus.SetCurrentValue(TextBlock.TextProperty, "Оффлайн");
        ImgAvatar.Source = null;
        ImgAvatarLarge.Source = null;
        ReloadSkin3DModel();
        if (BorderProfileCard != null) BorderProfileCard.Visibility = Visibility.Collapsed;
        if (BorderLoginCard != null) BorderLoginCard.Visibility = Visibility.Visible;
        if (CapesWrapPanel != null) CapesWrapPanel.Children.Clear();
        if (TxtSelectedSkinFileName != null) TxtSelectedSkinFileName.Text = "";

        BtnLogout.IsEnabled = false;
        BtnResetSkin.IsEnabled = false;
        TxtSkinStatus.Text = "";

        RefreshSidebarAccounts();
    }

}
