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
    private void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        _account = acc;

        TxtAccName.Text = acc.Username;

        _currentSkinModel = !string.IsNullOrEmpty(acc.SkinModel) ? acc.SkinModel : "classic";
        var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
        var badge = isSlim ? "Slim" : "Classic";
        if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = badge;

        if (ToggleModelSwitch != null) ToggleModelSwitch.IsChecked = isSlim;

        var sideStatus = acc.IsVec ? "VEC ID" : (acc.IsOffline ? "Оффлайн" : "Microsoft");

        TxtSideName.SetCurrentValue(TextBlock.TextProperty, acc.Username);
        TxtSideStatus.SetCurrentValue(TextBlock.TextProperty, sideStatus);

        if (BorderProfileCard != null) BorderProfileCard.Visibility = Visibility.Visible;
        if (BorderLoginCard != null) BorderLoginCard.Visibility = Visibility.Collapsed;

        if (acc.HasMinecraftLicense)
        {
            if (TxtLicenseBadge != null)
            {
                TxtLicenseBadge.Text = "Mojang";
                TxtLicenseBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                TxtLicenseBadge.Visibility = Visibility.Visible;
            }
            if (TxtMinecraftNick != null)
            {
                TxtMinecraftNick.Text = !string.IsNullOrEmpty(acc.MicrosoftGamertag) ? $"Minecraft: {acc.MicrosoftGamertag}" : $"Minecraft: {acc.Username}";
            }
        }
        else
        {
            if (TxtLicenseBadge != null)
            {
                TxtLicenseBadge.Text = "";
                TxtLicenseBadge.Visibility = Visibility.Collapsed;
            }
            if (TxtMinecraftNick != null)
            {
                TxtMinecraftNick.Text = $"ID: {acc.Username}";
            }
        }

        var avatar = GetAccountAvatarImage(acc, 48);
        if (avatar != null && ImgAvatarLarge != null) ImgAvatarLarge.Source = avatar;

        BtnLogout.IsEnabled = true;
        BtnResetSkin.IsEnabled = true;

        RefreshSidebarAccounts();
        _ = RefreshCapesGridUIAsync(acc);
        if (refreshSkin) _ = LoadSkinImagesAsync(acc);
    }
}
