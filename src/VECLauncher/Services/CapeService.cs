using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class CapeItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsUnlocked { get; set; }
    public bool IsEquipped { get; set; }
    public byte[]? RawTextureBytes { get; set; }
    public ImageSource? PreviewImage { get; set; }
}

public static class CapeService
{
    public static List<CapeItem> GetAllCapes(MinecraftAccount? account, byte[]? currentlyEquippedBytes)
    {
        var result = new List<CapeItem>();

        result.Add(new CapeItem
        {
            Id = "none",
            Name = "Без плаща",
            Description = "Снять плащ с персонажа.",
            IsUnlocked = true,
            IsEquipped = currentlyEquippedBytes == null || currentlyEquippedBytes.Length == 0,
            RawTextureBytes = null,
            PreviewImage = CreateEmptyPreview()
        });

        return result;
    }

    public static ImageSource? CreateCapePreview(byte[]? capeBytes)
    {
        if (capeBytes == null || capeBytes.Length == 0) return null;

        try
        {
            using var ms = new MemoryStream(capeBytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            var formatConverted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int srcW = formatConverted.PixelWidth;
            int srcH = formatConverted.PixelHeight;

            int scale = Math.Max(1, srcW / 64);
            int cropX = 1 * scale;
            int cropY = 1 * scale;
            int cropW = 10 * scale;
            int cropH = 16 * scale;

            if (cropX + cropW > srcW || cropY + cropH > srcH)
            {
                cropX = 0; cropY = 0; cropW = srcW; cropH = srcH;
            }

            int srcStride = srcW * 4;
            byte[] srcPixels = new byte[srcH * srcStride];
            formatConverted.CopyPixels(srcPixels, srcStride, 0);

            int targetW = 44;
            int targetH = 70;
            int targetStride = targetW * 4;
            byte[] targetPixels = new byte[targetH * targetStride];

            for (int y = 0; y < targetH; y++)
            {
                int srcY = cropY + (y * cropH) / targetH;
                if (srcY >= srcH) srcY = srcH - 1;

                for (int x = 0; x < targetW; x++)
                {
                    int srcX = cropX + (x * cropW) / targetW;
                    if (srcX >= srcW) srcX = srcW - 1;

                    int srcOffset = (srcY * srcW + srcX) * 4;
                    int targetOffset = (y * targetW + x) * 4;

                    targetPixels[targetOffset + 0] = srcPixels[srcOffset + 0];
                    targetPixels[targetOffset + 1] = srcPixels[srcOffset + 1];
                    targetPixels[targetOffset + 2] = srcPixels[srcOffset + 2];
                    targetPixels[targetOffset + 3] = srcPixels[srcOffset + 3];
                }
            }

            var writeable = new WriteableBitmap(targetW, targetH, 96, 96, PixelFormats.Bgra32, null);
            writeable.WritePixels(new System.Windows.Int32Rect(0, 0, targetW, targetH), targetPixels, targetStride, 0);
            writeable.Freeze();
            return writeable;
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to create cape preview: {ex.Message}");
            return null;
        }
    }

    private static ImageSource CreateEmptyPreview()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 
                             new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1), 
                             new System.Windows.Rect(0, 0, 60, 96));
            var ft = new FormattedText("✕", 
                System.Globalization.CultureInfo.CurrentCulture, 
                System.Windows.FlowDirection.LeftToRight, 
                new Typeface("Segoe UI"), 22, 
                new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 96);
            dc.DrawText(ft, new System.Windows.Point(23, 33));
        }

        var rtb = new RenderTargetBitmap(60, 96, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}