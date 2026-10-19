using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _selectedSkinFileName = "";

    [ObservableProperty]
    private string _skinStatus = "";

    [ObservableProperty]
    private Brush _skinStatusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isSlimModel;

    [ObservableProperty]
    private string _skinModelBadge = "Classic";

    public event Action<byte[]?, bool>? SkinTextureChanged;
    public event Action<bool>? SkinModelChanged;

    [RelayCommand]
    public async Task RefreshSkinAsync()
    {
        if (Account == null) return;

        SkinStatus = "Синхронизация с сервером...";
        SkinStatusColor = new SolidColorBrush(Color.FromRgb(0x9E, 0xA2, 0xB0));

        var (skinBytes, capeBytes, model) = await Skins.SyncSkinFromServerAsync(Account);

        if (!string.IsNullOrEmpty(model))
        {
            IsSlimModel = model.Equals("slim", StringComparison.OrdinalIgnoreCase);
            CurrentSkinModel = IsSlimModel ? "slim" : "classic";
            SkinModelBadge = IsSlimModel ? "Slim" : "Classic";
            Account.SkinModel = CurrentSkinModel;
        }

        if (skinBytes != null && skinBytes.Length > 0)
        {
            CurrentSkinRawBytes = skinBytes;
            CurrentAvatarImage = SkinService.CreateCrispHeadAvatar(skinBytes, 48);
            SkinTextureChanged?.Invoke(skinBytes, IsSlimModel);
        }

        if (capeBytes != null && capeBytes.Length > 0)
        {
            CurrentCapeRawBytes = capeBytes;
            InvokeCapeTextureChanged(capeBytes);
        }

        SkinStatus = "";
        Dialogs?.ShowToast("Синхронизация", "Скин и плащ успешно обновлены с сервера!", ToastType.Success);
    }

    [RelayCommand]
    public async Task BrowseAndUploadSkinAsync()
    {
        if (Account == null)
        {
            SkinStatus = "Сначала войдите в аккаунт.";
            SkinStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            Dialogs?.ShowToast("Авторизация", "Сначала войдите в аккаунт, чтобы сменить скин.", ToastType.Warning);
            return;
        }

        var filePath = Dialogs?.BrowseFile("PNG изображения (*.png)|*.png|Все файлы (*.*)|*.*", "Выберите PNG-файл скина");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        SkinStatus = "Применяю скин...";
        SkinStatusColor = new SolidColorBrush(Color.FromRgb(0x9E, 0xA2, 0xB0));

        IsInternalSkinWrite = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            SkinService.ValidateSkinPng(bytes);

            SelectedSkinFileName = Path.GetFileName(filePath);
            CurrentSkinRawBytes = bytes;

            var skinFile = SkinService.AccountSkinPath(Account.Username);
            await SkinService.SafeWriteAllBytesAsync(skinFile, bytes);

            if (Account.IsVec)
            {
                var srvUrl = !string.IsNullOrEmpty(Account.ServerUrl) ? Account.ServerUrl : VecAuthService.DefaultVecServerUrl;
                var modelEnum = IsSlimModel ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
                try
                {
                    await Skins.UploadSkinToVecServerAsync(srvUrl, Account.Username, bytes, modelEnum, Account.Uuid);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Skin upload failed: {ex.Message}");
                }
            }

            CurrentAvatarImage = SkinService.CreateCrispHeadAvatar(bytes, 48);
            SkinTextureChanged?.Invoke(bytes, IsSlimModel);

            SkinStatus = "";
            Dialogs?.ShowToast("Скин обновлён", "Новый скин успешно установлен!", ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка смены скина", ex);
            SkinStatus = ex.Message;
            SkinStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            await Task.Delay(500);
            IsInternalSkinWrite = false;
        }
    }

    [RelayCommand]
    public async Task ResetSkinAsync()
    {
        if (Account == null) return;

        if (Dialogs != null && !await Dialogs.ConfirmAsync("Подтверждение", "Сбросить скин на стандартный?"))
            return;

        IsInternalSkinWrite = true;
        try
        {
            var skinFile = SkinService.AccountSkinPath(Account.Username);
            SkinService.SafeDelete(skinFile);

            CurrentSkinRawBytes = null;
            CurrentAvatarImage = null;

            if (Account.IsVec)
            {
                try
                {
                    var srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                    await Skins.ResetSkinOnVecServerAsync(srvUrl, Account.Username, null, CurrentSkinModel);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            SkinTextureChanged?.Invoke(null, IsSlimModel);
            SkinStatus = "";
            Dialogs?.ShowToast("Скин сброшен", "Скин сброшен на стандартный.", ToastType.Info);
        }
        catch (Exception ex)
        {
            SkinStatus = ex.Message;
            SkinStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            await Task.Delay(500);
            IsInternalSkinWrite = false;
        }
    }

    [RelayCommand]
    public async Task ToggleSkinModelAsync()
    {
        IsSlimModel = !IsSlimModel;
        CurrentSkinModel = IsSlimModel ? "slim" : "classic";
        SkinModelBadge = IsSlimModel ? "Slim" : "Classic";

        if (Account != null)
        {
            Account.SkinModel = CurrentSkinModel;
            Accounts.Save(Account);

            if (Account.IsVec)
            {
                IsInternalSkinWrite = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var srvUrl = (!string.IsNullOrEmpty(Account.ServerUrl) && !Account.ServerUrl.Contains("95.59.233"))
                            ? Account.ServerUrl
                            : await VecAuthService.GetActiveServerUrlAsync();
                        await Skins.UpdateSkinModelOnVecServerAsync(srvUrl, Account.Username, CurrentSkinModel);

                        byte[]? skinBytes = CurrentSkinRawBytes;
                        if (skinBytes == null || skinBytes.Length == 0)
                        {
                            var skinFile = SkinService.FindAccountSkin(Account.Username);
                            if (skinFile != null && File.Exists(skinFile))
                            {
                                skinBytes = await SkinService.SafeReadAllBytesAsync(skinFile);
                            }
                        }

                        if (skinBytes != null && skinBytes.Length > 0)
                        {
                            var modelEnum = IsSlimModel ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
                            await Skins.UploadSkinToVecServerAsync(srvUrl, Account.Username, skinBytes, modelEnum, Account.Uuid);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Sync model to server failed: {ex.Message}");
                    }
                    finally
                    {
                        await Task.Delay(500);
                        IsInternalSkinWrite = false;
                    }
                });
            }
        }

        SkinModelChanged?.Invoke(IsSlimModel);
    }

    public void InvokeCapeTextureChanged(byte[]? bytes)
    {
        CapeTextureChanged?.Invoke(bytes);
    }
}
