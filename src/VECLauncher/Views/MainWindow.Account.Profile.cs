using VECLauncher.Models;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private void SetAccount(MinecraftAccount acc, bool refreshSkin)
    {
        Vm.SetAccount(acc, refreshSkin);
    }

    private void OnAccountChanged(MinecraftAccount? acc, bool refreshSkin)
    {
        if (acc != null)
        {
            _account = acc;
            _currentSkinModel = !string.IsNullOrEmpty(acc.SkinModel) ? acc.SkinModel : "classic";
            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
            if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = isSlim ? "Slim" : "Classic";
            if (ToggleModelSwitch != null) ToggleModelSwitch.IsChecked = isSlim;

            if (BtnResetSkin != null) BtnResetSkin.IsEnabled = true;
            _ = RefreshCapesGridUIAsync(acc);
            if (refreshSkin) _ = LoadSkinImagesAsync(acc);
        }
        else
        {
            _account = null;
            _currentSkinRawBytes = null;
            ReloadSkin3DModel();
            if (TxtSelectedSkinFileName != null) TxtSelectedSkinFileName.Text = "";
            if (BtnResetSkin != null) BtnResetSkin.IsEnabled = false;
            if (TxtSkinStatus != null) TxtSkinStatus.Text = "";
        }
    }
}
