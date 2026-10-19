using IOPath = System.IO.Path;
using System.IO;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private void OnSkinTextureChanged(byte[]? skinBytes, bool isSlim)
    {
        _currentSkinRawBytes = skinBytes;
        _currentSkinModel = isSlim ? "slim" : "classic";
        ReloadSkin3DModel();
        if (_currentCapeRawBytes != null)
        {
            _gltfSkinModel?.UpdateCapeTexture(_currentCapeRawBytes);
        }
    }

    private void OnSkinModelChanged(bool isSlim)
    {
        _currentSkinModel = isSlim ? "slim" : "classic";
        ReloadSkin3DModel();
        if (_currentCapeRawBytes != null)
        {
            _gltfSkinModel?.UpdateCapeTexture(_currentCapeRawBytes);
        }
    }

    public Task LoadSkinImagesAsync(MinecraftAccount acc)
    {
        return Vm.RefreshSkinCommand.ExecuteAsync(null);
    }
}
