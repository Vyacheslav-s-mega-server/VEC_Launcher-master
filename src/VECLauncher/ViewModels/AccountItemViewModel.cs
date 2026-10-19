using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VECLauncher.Models;

namespace VECLauncher.ViewModels;

public partial class AccountItemViewModel : ObservableObject
{
    public MinecraftAccount Account { get; }
    public string Username => Account.Username;
    public string TypeDisplay => Account.IsVec ? "VEC ID" : (Account.IsOffline ? "Оффлайн" : "Microsoft");
    public string Initial => Account.IsVec ? "V" : (Account.IsOffline ? "O" : "M");

    [ObservableProperty]
    private ImageSource? _avatar;

    [ObservableProperty]
    private bool _isCurrent;

    public bool HasAvatar => Avatar != null;

    public Brush BackgroundBrush => IsCurrent 
        ? new SolidColorBrush(Color.FromRgb(22, 22, 30)) 
        : Brushes.Transparent;

    public FontWeight NameFontWeight => IsCurrent ? FontWeights.Bold : FontWeights.Normal;

    public Brush NameForeground => IsCurrent 
        ? Brushes.White 
        : new SolidColorBrush(Color.FromRgb(180, 180, 190));

    public Visibility CurrentCheckVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public AccountItemViewModel(MinecraftAccount account, bool isCurrent, ImageSource? avatar = null)
    {
        Account = account;
        _isCurrent = isCurrent;
        _avatar = avatar;
    }

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(NameFontWeight));
        OnPropertyChanged(nameof(NameForeground));
        OnPropertyChanged(nameof(CurrentCheckVisibility));
    }

    partial void OnAvatarChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasAvatar));
    }
}
