using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class CapeItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "VEC";
    public string? RequiredCode { get; set; }
    public bool IsDefaultUnlocked { get; set; }
    public bool IsUnlocked { get; set; }
    public bool IsEquipped { get; set; }
    public bool IsOfficialMojang { get; set; }
    public byte[]? RawTextureBytes { get; set; }
    public ImageSource? PreviewImage { get; set; }
}

public static class CapeService
{
    private static readonly string StoragePath = Path.Combine(LauncherPaths.Root, "unlocked_capes.json");

    public static List<string> GetUnlockedCapeIds(string username)
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (dict != null && dict.TryGetValue(username.ToLowerInvariant(), out var list))
                {
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to load unlocked capes for {username}: {ex.Message}");
        }

        return new List<string>();
    }

    public static void UnlockCape(string username, string capeId)
    {
        try
        {
            var dict = new Dictionary<string, List<string>>();
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
            }

            var key = username.ToLowerInvariant();
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<string>();
                dict[key] = list;
            }

            if (!list.Contains(capeId, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(capeId);
                var dir = Path.GetDirectoryName(StoragePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StoragePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                Log.Info($"CapeService: unlocked cape '{capeId}' for user '{username}'");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to unlock cape '{capeId}' for {username}: {ex.Message}");
        }
    }

    public static List<CapeItem> GetAllCapes(MinecraftAccount? account, byte[]? currentlyEquippedBytes)
    {
        var result = new List<CapeItem>();

        result.Add(new CapeItem
        {
            Id = "none",
            Name = "Без плаща",
            Description = "Снять плащ с персонажа.",
            Category = "Базовые",
            IsDefaultUnlocked = true,
            IsUnlocked = true,
            IsEquipped = currentlyEquippedBytes == null || currentlyEquippedBytes.Length == 0,
            RawTextureBytes = null,
            PreviewImage = CreateEmptyPreview()
        });

        var vecBytes = LoadEmbeddedCape("cape_vec.png") ?? DefaultSkinService.GetDefaultCape();
        if (vecBytes != null)
        {
            var isEq = currentlyEquippedBytes != null && currentlyEquippedBytes.Length > 0 &&
                       (currentlyEquippedBytes.SequenceEqual(vecBytes) || (currentlyEquippedBytes.Length == vecBytes.Length && currentlyEquippedBytes[0] == vecBytes[0]));
            result.Add(new CapeItem
            {
                Id = "vec_default",
                Name = "Плащ VEC (КПВК)",
                Description = "Официальный плащ инженеров и студентов VEC КПВК с эмблемой и поддержкой элитр.",
                Category = "Эксклюзив VEC",
                IsDefaultUnlocked = true,
                IsUnlocked = true,
                IsEquipped = isEq,
                RawTextureBytes = vecBytes,
                PreviewImage = CreateCapePreview(vecBytes)
            });
        }

        return result;
    }

    private static byte[]? LoadEmbeddedCape(string fileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = $"VECLauncher.Assets.{fileName}";
            using var s = asm.GetManifestResourceStream(resName);
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            var local = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(local)) return File.ReadAllBytes(local);
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to load embedded cape '{fileName}': {ex.Message}");
        }

        return null;
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