using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VECLauncher.Services;

public static class ThemeService
{
    public static Color CurrentAccent { get; private set; } =
        (Color)ColorConverter.ConvertFromString("#FACC15");

    public static void ApplyAccent(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            if (!hex.StartsWith('#')) hex = "#" + hex;

            var color = (Color)ColorConverter.ConvertFromString(hex);
            CurrentAccent = color;

            var res = Application.Current?.Resources;
            if (res is null) return;

            res["AccentColor"] = color;
            res["Accent"] = Freeze(new SolidColorBrush(color));
            res["AccentDark"] = Freeze(new SolidColorBrush(Darken(color, 0.82)));
            res["AccentLight"] = Freeze(new SolidColorBrush(Lighten(color, 0.22)));
            res["AccentGlow"] = Freeze(new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)));
        }
        catch (Exception ex)
        {
            Log.Warn($"ThemeService: failed to apply accent color: {ex.Message}");
        }
    }

    public static ImageBrush? BuildWindowBackground(string imagePath, double opacity)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath);
            bmp.DecodePixelWidth = 1920;
            bmp.EndInit();
            bmp.Freeze();

            var brush = new ImageBrush(bmp)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Opacity = Math.Clamp(opacity, 0.05, 1.0)
            };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            Log.Warn($"ThemeService: failed to load window background: {ex.Message}");
            return null;
        }
    }

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public static Color Darken(Color c, double factor) => Color.FromRgb(
        (byte)Math.Clamp(c.R * factor, 0, 255),
        (byte)Math.Clamp(c.G * factor, 0, 255),
        (byte)Math.Clamp(c.B * factor, 0, 255));

    public static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
        (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
        (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));
}