using System;
using System.IO;
using System.Threading.Tasks;
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
    private Brush _skinStatusColor = Brushes.Transparent;

    [ObservableProperty]
    private bool _isSlimModel;

    [ObservableProperty]
    private string _skinModelBadge = "Classic";

    public event Action<byte[]?, bool>? SkinTextureChanged;
    public event Action<bool>? SkinModelChanged;

    partial void OnIsSlimModelChanged(bool value)
    {
        CurrentSkinModel = value ? "slim" : "classic";
        SkinModelBadge = value ? "Slim" : "Classic";
        SkinModelChanged?.Invoke(value);

        if (Account != null)
        {
            Account.SkinModel = CurrentSkinModel;
            Accounts.Save(Account);

            if (Account.IsVms)
            {
                IsInternalSkinWrite = true;
                LastInternalSkinWriteTime = DateTime.UtcNow;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string srvUrl = (!string.IsNullOrEmpty(Account.ServerUrl) && !Account.ServerUrl.Contains("95.59.233"))
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
                            var modelEnum = value ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
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
    }

    [RelayCommand]
    public void ToggleSkinModel()
    {
        IsSlimModel = !IsSlimModel;
    }

    [RelayCommand]
    public async Task RefreshSkinAsync()
    {
        if (Account == null) return;

        IsInternalSkinWrite = true;
        LastInternalSkinWriteTime = DateTime.UtcNow;
        try
        {
            var (skinBytes, capeBytes, model) = await Skins.SyncSkinFromServerAsync(Account);

            if (!string.IsNullOrEmpty(model))
            {
                IsSlimModel = model.Equals("slim", StringComparison.OrdinalIgnoreCase);
                CurrentSkinModel = IsSlimModel ? "slim" : "classic";
                SkinModelBadge = IsSlimModel ? "Slim" : "Classic";
                Account.SkinModel = CurrentSkinModel;
                Accounts.Save(Account);
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

            await RefreshWardrobeAsync();
            Dialogs?.ShowToast("Синхронизация", "Скин и плащ успешно обновлены с сервера!", ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"RefreshSkinAsync error: {ex.Message}");
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            await Task.Delay(500);
            IsInternalSkinWrite = false;
        }
    }

    [RelayCommand]
    public async Task BrowseAndUploadSkinAsync()
    {
        if (Account == null)
        {
            Dialogs?.ShowToast("Авторизация", "Сначала войдите в аккаунт, чтобы сменить скин.", ToastType.Warning);
            return;
        }

        var filePath = Dialogs?.BrowseFile("PNG изображения (*.png)|*.png|Все файлы (*.*)|*.*", "Выберите PNG-файл скина");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        IsInternalSkinWrite = true;
        LastInternalSkinWriteTime = DateTime.UtcNow;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            SkinService.ValidateSkinPng(bytes);

            SelectedSkinFileName = Path.GetFileName(filePath);
            CurrentSkinRawBytes = bytes;

            string skinFile = SkinService.AccountSkinPath(Account.Username);
            await SkinService.SafeWriteAllBytesAsync(skinFile, bytes);

            if (Account.IsVms)
            {
                string srvUrl = !string.IsNullOrEmpty(Account.ServerUrl) ? Account.ServerUrl : VecAuthService.DefaultVecServerUrl;
                SkinService.SkinModel modelEnum = IsSlimModel ? SkinService.SkinModel.Slim : SkinService.SkinModel.Classic;
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

            Dialogs?.ShowToast("Скин обновлён", "Новый скин успешно установлен!", ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка смены скина", ex);
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
        LastInternalSkinWriteTime = DateTime.UtcNow;
        try
        {
            var skinFile = SkinService.AccountSkinPath(Account.Username);
            SkinService.SafeDelete(skinFile);

            CurrentSkinRawBytes = null;
            CurrentAvatarImage = null;

            if (Account.IsVms)
            {
                try
                {
                    string srvUrl = await VecAuthService.GetActiveServerUrlAsync();
                    await Skins.ResetSkinOnVecServerAsync(srvUrl, Account.Username, null, CurrentSkinModel);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            SkinTextureChanged?.Invoke(null, IsSlimModel);
            Dialogs?.ShowToast("Скин сброшен", "Скин сброшен на стандартный.", ToastType.Info);
        }
        catch (Exception ex)
        {
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            await Task.Delay(500);
            IsInternalSkinWrite = false;
        }
    }

    public void InvokeCapeTextureChanged(byte[]? bytes)
    {
        CapeTextureChanged?.Invoke(bytes);
    }
}
