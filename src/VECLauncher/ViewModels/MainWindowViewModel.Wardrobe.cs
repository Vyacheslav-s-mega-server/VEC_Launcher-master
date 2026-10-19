using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<CapeItemViewModel> AvailableCapes { get; } = new();

    [ObservableProperty]
    private string _capePromoCode = "";

    [ObservableProperty]
    private string _capePromoStatus = "";

    [ObservableProperty]
    private Brush _capePromoStatusColor = Brushes.Transparent;

    [ObservableProperty]
    private bool _isCapePromoStatusVisible;

    [ObservableProperty]
    private bool _isRedeemingCapePromo;

    public event Action<byte[]?>? CapeTextureChanged;

    private int _wardrobeRefreshSeq = 0;

    [RelayCommand]
    public async Task RefreshWardrobeAsync()
    {
        var acc = Account;
        if (acc == null)
        {
            AvailableCapes.Clear();
            return;
        }

        var seq = System.Threading.Interlocked.Increment(ref _wardrobeRefreshSeq);
        var state = await Capes.GetWardrobeStateAsync(acc);
        if (seq != _wardrobeRefreshSeq) return;

        if (!string.IsNullOrEmpty(state.SkinModel))
        {
            CurrentSkinModel = state.SkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";
            acc.SkinModel = CurrentSkinModel;
        }

        if (state.HasMinecraftLicense != HasMinecraftLicense)
        {
            acc.HasMinecraftLicense = state.HasMinecraftLicense;
            HasMinecraftLicense = state.HasMinecraftLicense;
            MinecraftNickDisplay = HasMinecraftLicense
                ? (!string.IsNullOrEmpty(acc.MicrosoftGamertag) ? $"Minecraft: {acc.MicrosoftGamertag}" : $"Minecraft: {acc.Username}")
                : $"ID: {acc.Username}";
        }

        if (!string.IsNullOrEmpty(state.MicrosoftGamertag))
        {
            acc.MicrosoftGamertag = state.MicrosoftGamertag;
        }

        AvailableCapes.Clear();
        foreach (var c in state.Capes)
        {
            ImageSource? preview = null;
            if (c.RawBytes != null)
            {
                preview = CapeService.CreateCapePreview(c.RawBytes);
            }

            var vm = new CapeItemViewModel(c, c.IsEquipped, preview);
            AvailableCapes.Add(vm);

            if (preview == null && !string.IsNullOrEmpty(c.PreviewUrl))
            {
                _ = Task.Run(async () =>
                {
                    var bytes = await Capes.DownloadCapeTextureAsync(c.PreviewUrl);
                    if (bytes != null && bytes.Length > 0)
                    {
                        c.RawBytes = bytes;
                        var p = CapeService.CreateCapePreview(bytes);
                        if (p != null)
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.PreviewImage = p);
                        }
                    }
                });
            }
        }
    }

    [RelayCommand]
    public async Task EquipCapeAsync(CapeItemViewModel? item)
    {
        if (item == null || Account == null) return;

        foreach (var c in AvailableCapes)
        {
            c.IsEquipped = c.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase);
        }

        IsInternalSkinWrite = true;
        LastInternalSkinWriteTime = DateTime.UtcNow;
        try
        {
            if (item.Id == "none")
            {
                CurrentCapeRawBytes = null;
                CapeTextureChanged?.Invoke(null);
                Dialogs?.ShowToast("Гардероб", "Плащ снят.", ToastType.Info);

                await Capes.RemoveCapeAsync(Account);
                return;
            }

            byte[]? capeBytes = item.RawBytes;
            if (capeBytes == null && !string.IsNullOrEmpty(item.PreviewUrl))
            {
                capeBytes = await Capes.DownloadCapeTextureAsync(item.PreviewUrl);
                if (capeBytes != null) item.Info.RawBytes = capeBytes;
            }

            if (capeBytes != null && capeBytes.Length > 0)
            {
                CurrentCapeRawBytes = capeBytes;
                CapeTextureChanged?.Invoke(capeBytes);
                Dialogs?.ShowToast("Гардероб", $"Плащ «{item.Name}» успешно надет!", ToastType.Success);

                await Capes.EquipCapeAsync(Account, item.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Equip cape failed: {ex.Message}");
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
            _ = RefreshWardrobeAsync();
        }
        finally
        {
            await Task.Delay(500);
            IsInternalSkinWrite = false;
        }
    }

    [RelayCommand]
    public async Task RedeemCapePromoAsync()
    {
        var code = CapePromoCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            CapePromoStatus = "Введите промокод.";
            CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            IsCapePromoStatusVisible = true;
            return;
        }

        if (Account == null)
        {
            CapePromoStatus = "Сначала войдите в аккаунт.";
            CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            IsCapePromoStatusVisible = true;
            return;
        }

        IsRedeemingCapePromo = true;
        CapePromoStatus = "Проверка промокода...";
        CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0x9E, 0xA2, 0xB0));
        IsCapePromoStatusVisible = true;

        try
        {
            var (success, msg) = await Capes.RedeemCapePromoAsync(Account, code);
            if (success)
            {
                CapePromoStatus = msg;
                CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                CapePromoCode = "";
                Dialogs?.ShowToast("Промокод активирован", msg, ToastType.Success);
                await RefreshWardrobeAsync();
            }
            else
            {
                CapePromoStatus = !string.IsNullOrEmpty(msg) ? msg : "Неверный промокод.";
                CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                Dialogs?.ShowToast("Ошибка промокода", CapePromoStatus, ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            CapePromoStatus = $"Ошибка: {ex.Message}";
            CapePromoStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            Dialogs?.ShowToast("Ошибка", ex.Message, ToastType.Error);
        }
        finally
        {
            IsRedeemingCapePromo = false;
        }
    }
}
