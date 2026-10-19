using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VECLauncher.ViewModels;

public partial class ContentTileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private string _versionTag = "";

    [ObservableProperty]
    private string _sizeTag = "";

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private bool _isRequired;

    [ObservableProperty]
    private bool _isServerItem;

    [ObservableProperty]
    private string? _modrinthSlug;

    [ObservableProperty]
    private string _toggleText = "";

    [ObservableProperty]
    private string _iconLetter = "V";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultIconVisibility))]
    private BitmapImage? _iconImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultBannerVisibility))]
    private BitmapImage? _bannerImage;

    public Visibility DefaultIconVisibility => IconImage is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DefaultBannerVisibility => BannerImage is null ? Visibility.Visible : Visibility.Collapsed;
}
