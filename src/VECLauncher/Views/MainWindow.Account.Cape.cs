using System.IO;
using System.Windows.Input;
using VECLauncher.Models;
using VECLauncher.Services;
using IOPath = System.IO.Path;

namespace VECLauncher.Views;

public partial class MainWindow
{
    public Task RefreshCapesGridUIAsync(MinecraftAccount? acc)
    {
        return Vm.RefreshWardrobeCommand.ExecuteAsync(null);
    }

    private void TxtCapePromoCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm.RedeemCapePromoCommand.Execute(null);
        }
    }

    private void OnCapeTextureChanged(byte[]? capeBytes)
    {
        _currentCapeRawBytes = capeBytes;
        _gltfSkinModel?.UpdateCapeTexture(capeBytes);

        if (_account != null)
        {
            var capePath = SkinService.AccountCapePath(_account.Username);
            _isInternalSkinWrite = true;
            try
            {
                lock (_skinFileLock)
                {
                    if (capeBytes != null && capeBytes.Length > 0)
                    {
                        var dir = IOPath.GetDirectoryName(capePath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(capePath, capeBytes);
                    }
                    else if (File.Exists(capePath))
                    {
                        File.Delete(capePath);
                    }
                }
            }
            finally
            {
                _isInternalSkinWrite = false;
            }
        }
    }
}
