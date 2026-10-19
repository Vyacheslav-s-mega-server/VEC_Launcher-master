using System.IO;
using System.Windows.Input;
using VECLauncher.Models;
using VECLauncher.Services;
using VECLauncher.ViewModels;
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
            MainWindowViewModel.IsInternalSkinWrite = true;
            try
            {
                lock (_skinFileLock)
                {
                    if (capeBytes != null && capeBytes.Length > 0)
                    {
                        var dir = IOPath.GetDirectoryName(capePath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        using var fs = new FileStream(capePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                        fs.Write(capeBytes, 0, capeBytes.Length);
                    }
                    else
                    {
                        SkinService.SafeDelete(capePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"OnCapeTextureChanged write error: {ex.Message}");
            }
            finally
            {
                Task.Delay(500).ContinueWith(_ =>
                {
                    _isInternalSkinWrite = false;
                    MainWindowViewModel.IsInternalSkinWrite = false;
                });
            }
        }
    }
}
