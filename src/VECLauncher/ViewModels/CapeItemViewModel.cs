using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

public partial class CapeItemViewModel : ObservableObject
{
    public CapeInfo Info { get; }
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public string? PreviewUrl => Info.PreviewUrl;
    public byte[]? RawBytes => Info.RawBytes;

    public bool IsNoneCape => Id == "none";
    public bool IsNotNoneCape => Id != "none";

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private bool _isEquipped;

    public Brush CardBackground => IsEquipped 
        ? new SolidColorBrush(Color.FromRgb(0x27, 0x2A, 0x34)) 
        : new SolidColorBrush(Color.FromRgb(0x23, 0x25, 0x2D));

    public Brush CardBorder => IsEquipped 
        ? new SolidColorBrush(Color.FromRgb(0x55, 0x5C, 0x70)) 
        : new SolidColorBrush(Color.FromRgb(0x32, 0x36, 0x42));

    public Brush RadioBorderBrush => IsEquipped 
        ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) 
        : new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

    public Visibility RadioDotVisibility => IsEquipped ? Visibility.Visible : Visibility.Collapsed;

    public FontWeight TitleFontWeight => IsEquipped ? FontWeights.SemiBold : FontWeights.Normal;

    public Brush TitleForeground => IsEquipped 
        ? Brushes.White 
        : new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));

    public CapeItemViewModel(CapeInfo info, bool isEquipped, ImageSource? previewImage = null)
    {
        Info = info;
        _isEquipped = isEquipped;
        _previewImage = previewImage;
    }

    partial void OnIsEquippedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorder));
        OnPropertyChanged(nameof(RadioBorderBrush));
        OnPropertyChanged(nameof(RadioDotVisibility));
        OnPropertyChanged(nameof(TitleFontWeight));
        OnPropertyChanged(nameof(TitleForeground));
    }
}
