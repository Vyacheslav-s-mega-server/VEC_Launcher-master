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
    private async Task LoadSkinImagesAsync(MinecraftAccount acc)
    {
        try
        {
            ImageSource? localAvatar = null;
            byte[]? skinRawBytes = null;
            byte[]? capeRawBytes = null;
            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

            if (acc.IsVec)
            {
                try
                {
                    var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233")) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                    acc.ServerUrl = srvUrl;
                    var skinUrl = $"{srvUrl}/api/skin/{Uri.EscapeDataString(acc.Username)}.png?t={DateTime.UtcNow.Ticks}";
                    using var sResp = await App.Http.GetAsync(skinUrl);
                    if (sResp.IsSuccessStatusCode)
                    {
                        skinRawBytes = await sResp.Content.ReadAsByteArrayAsync();
                        var skinPath = IOPath.Combine(LauncherPaths.Root, "skins", $"{acc.Username}.png");
                        var dir = IOPath.GetDirectoryName(skinPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        _isInternalSkinWrite = true;
                        try { lock (_skinFileLock) { File.WriteAllBytes(skinPath, skinRawBytes); } }
                        finally { _isInternalSkinWrite = false; }
                    }

                    var capeUrl = $"{srvUrl}/api/cape/{Uri.EscapeDataString(acc.Username)}.png?t={DateTime.UtcNow.Ticks}";
                    using var cResp = await App.Http.GetAsync(capeUrl);
                    if (cResp.IsSuccessStatusCode)
                    {
                        capeRawBytes = await cResp.Content.ReadAsByteArrayAsync();
                        var capePath = IOPath.Combine(LauncherPaths.Root, "capes", $"{acc.Username}.png");
                        var dir = IOPath.GetDirectoryName(capePath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        _isInternalSkinWrite = true;
                        try { lock (_skinFileLock) { File.WriteAllBytes(capePath, capeRawBytes); } }
                        finally { _isInternalSkinWrite = false; }
                    }
                    else if (cResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        var capePath = IOPath.Combine(LauncherPaths.Root, "capes", $"{acc.Username}.png");
                        if (File.Exists(capePath))
                        {
                            _isInternalSkinWrite = true;
                            try { lock (_skinFileLock) { File.Delete(capePath); } }
                            finally { _isInternalSkinWrite = false; }
                        }
                        capeRawBytes = null;
                    }

                    try
                    {
                        var metaUrl = $"{srvUrl}/api/skin/{Uri.EscapeDataString(acc.Username)}.json?t={DateTime.UtcNow.Ticks}";
                        using var mResp = await App.Http.GetAsync(metaUrl);
                        if (mResp.IsSuccessStatusCode)
                        {
                            var metaJson = await mResp.Content.ReadAsStringAsync();
                            using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                            if (doc.RootElement.TryGetProperty("model", out var mProp))
                            {
                                var m = mProp.GetString();
                                if (!string.IsNullOrEmpty(m))
                                {
                                    _currentSkinModel = m.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";
                                    acc.SkinModel = _currentSkinModel;
                                }
                            }
                        }
                    }
                    catch { }

                    var metaFile = IOPath.Combine(LauncherPaths.Root, "skins", $"{acc.Username}.json");
                    if (File.Exists(metaFile))
                    {
                        try
                        {
                            var metaJson = File.ReadAllText(metaFile);
                            using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                            if (doc.RootElement.TryGetProperty("model", out var mProp))
                            {
                                var m = mProp.GetString();
                                if (!string.IsNullOrEmpty(m))
                                {
                                    _currentSkinModel = m.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";
                                    acc.SkinModel = _currentSkinModel;
                                }
                            }
                        }
                        catch { }
                    }
                    else if (!string.IsNullOrEmpty(acc.SkinModel))
                    {
                        _currentSkinModel = acc.SkinModel;
                    }

                    isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
                    Dispatcher.Invoke(() =>
                    {
                        if (ToggleModelSwitch != null)
                            ToggleModelSwitch.IsChecked = isSlim;
                        if (TxtToggleModelBadge != null)
                            TxtToggleModelBadge.Text = isSlim ? "Slim" : "Classic";
                    });
                }
                catch (Exception exVec)
                {
                    Log.Warn($"VEC skin sync: {exVec.Message}");
                }
            }

            if (skinRawBytes == null)
            {
                var localFile = IOPath.Combine(LauncherPaths.Root, "skins", $"{acc.Username}.png");
                if (File.Exists(localFile))
                {
                    try { skinRawBytes = File.ReadAllBytes(localFile); }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }

            if (capeRawBytes == null)
            {
                var localCape = IOPath.Combine(LauncherPaths.Root, "capes", $"{acc.Username}.png");
                if (File.Exists(localCape))
                {
                    try { capeRawBytes = File.ReadAllBytes(localCape); }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }

            if (skinRawBytes != null && skinRawBytes.Length > 0)
            {
                localAvatar = SkinService.CreateCrispHeadAvatar(skinRawBytes, 64);
            }

            _currentSkinRawBytes = skinRawBytes;
            _currentCapeRawBytes = capeRawBytes;

            if (skinRawBytes != null && skinRawBytes.Length > 0 && !acc.IsOffline)
            {
                try
                {
                    var skinPath = SkinService.AccountSkinPath(acc.Username);
                    var dir = System.IO.Path.GetDirectoryName(skinPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    _isInternalSkinWrite = true;
                    try { File.WriteAllBytes(skinPath, skinRawBytes); }
                    finally { _isInternalSkinWrite = false; }
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            Dispatcher.Invoke(() =>
            {
                ImgAvatar.Source = localAvatar;
                if (ImgAvatarLarge != null) ImgAvatarLarge.Source = localAvatar;
            });

            ReloadSkin3DModel();
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось загрузить скин: " + ex.Message);
        }
    }

    private static BitmapImage ToImage(byte[] data)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(data);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }



    private async void BtnRefreshSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        TxtSkinStatus.Text = "Синхронизация с сервером...";
        TxtSkinStatus.Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray);
        await LoadSkinImagesAsync(_account);
        TxtSkinStatus.Text = "";
        _toast.Show("Синхронизация", "Скин и плащ успешно обновлены с сервера!", NotificationType.Success);
    }

    private async void BtnBrowseSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null)
        {
            _toast.Show("Авторизация", "Сначала войдите в аккаунт, чтобы сменить скин.", NotificationType.Warning);
            TxtSkinStatus.Text = "Сначала войдите в аккаунт.";
            TxtSkinStatus.Foreground = (Brush)(TryFindResource("Danger") ?? Brushes.Red);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Выберите PNG-файл скина (64x64 или 64x32)",
            Filter = "PNG изображения (*.png)|*.png|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        TxtSkinStatus.Text = "Применяю скин...";
        TxtSkinStatus.Foreground = (Brush)(TryFindResource("FgMuted") ?? Brushes.Gray);

        try
        {
            var bytes = await File.ReadAllBytesAsync(dlg.FileName);
            SkinService.ValidateSkinPng(bytes);
            if (TxtSelectedSkinFileName != null) TxtSelectedSkinFileName.Text = IOPath.GetFileName(dlg.FileName);

            _currentSkinRawBytes = bytes;
            ReloadSkin3DModel();

            var model = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase) ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;

            if (_account != null)
            {
                _account.SkinModel = _currentSkinModel;
                Vm.Accounts.Save(_account);
            }

            var skinFile = SkinService.AccountSkinPath(_account.Username);
            var capeFile = SkinService.FindAccountCape(_account.Username);
            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);

            var skinDir = IOPath.GetDirectoryName(skinFile);
            if (!string.IsNullOrEmpty(skinDir)) Directory.CreateDirectory(skinDir);
            lock (_skinFileLock) { File.WriteAllBytes(skinFile, bytes); }

            if (_account.IsVec)
            {
                var serverUrl = !string.IsNullOrEmpty(_account.ServerUrl) ? _account.ServerUrl : VecAuthService.DefaultVecServerUrl;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var uploadOk = await _skins.UploadSkinToVecServerAsync(serverUrl, _account.Username, bytes, model, _account.Uuid);
                        if (!uploadOk)
                            Log.Warn($"[BtnBrowseSkin] Skin upload FAILED for {_account.Username} on {serverUrl}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Не удалось отправить скин на сервер VEC: {ex.Message}");
                    }
                });
            }

            TxtSkinStatus.Text = "";
            _toast.Show("Скин обновлён", "Новый скин успешно установлен!", NotificationType.Success);
            await LoadSkinImagesAsync(_account);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка смены скина", ex);
            TxtSkinStatus.Text = ex.Message;
            TxtSkinStatus.Foreground = (Brush)(TryFindResource("Danger") ?? Brushes.Red);
            _toast.Show("Ошибка", ex.Message, NotificationType.Error);
        }
    }

    private async void BtnResetSkin_Click(object sender, RoutedEventArgs e)
    {
        if (_account is null) return;

        if (!await _dialog.ConfirmAsync("Подтверждение", "Сбросить скин на стандартный?")) return;

        BtnResetSkin.IsEnabled = false;
        try
        {
            var skinFile = SkinService.AccountSkinPath(_account.Username);
            if (File.Exists(skinFile)) File.Delete(skinFile);

            _currentSkinRawBytes = null;

            if (_account.IsVec)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                        await _skins.ResetSkinOnVecServerAsync(srvUrl, _account.Username, null, _currentSkinModel);
                    }
                    catch (Exception ex) { Log.Warn(ex.Message); }
                });
            }

            TxtSkinStatus.Text = "";
            await LoadSkinImagesAsync(_account);
        }
        catch (Exception ex)
        {
            TxtSkinStatus.Text = ex.Message;
            TxtSkinStatus.Foreground = (Brush)FindResource("Danger");
        }
        finally
        {
            BtnResetSkin.IsEnabled = true;
        }
    }

    private void ToggleModelSwitch_Click(object sender, RoutedEventArgs e)
    {
        var isSlim = ToggleModelSwitch.IsChecked == true;
        _currentSkinModel = isSlim ? "slim" : "classic";
        var badge = isSlim ? "Slim" : "Classic";
        if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = badge;

        if (_account != null)
        {
            _account.SkinModel = _currentSkinModel;
            Vm.Accounts.Save(_account);
            if (_account.IsVec)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var srvUrl = (!string.IsNullOrEmpty(_account.ServerUrl) && !_account.ServerUrl.Contains("95.59.233")) ? _account.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                        await _skins.UpdateSkinModelOnVecServerAsync(srvUrl, _account.Username, _currentSkinModel);

                        var skinFile = SkinService.FindAccountSkin(_account.Username);
                        if (skinFile != null && File.Exists(skinFile))
                        {
                            var modelEnum = isSlim ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
                            var skinBytes = await File.ReadAllBytesAsync(skinFile);
                            await _skins.UploadSkinToVecServerAsync(srvUrl, _account.Username, skinBytes, modelEnum, _account.Uuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Sync model to server failed: {ex.Message}");
                    }
                });
            }
        }

        ReloadSkin3DModel();
        if (_currentCapeRawBytes != null)
        {
            _gltfSkinModel.UpdateCapeTexture(_currentCapeRawBytes);
        }
    }
}
