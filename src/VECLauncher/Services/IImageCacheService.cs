using System.Windows.Media.Imaging;

namespace VECLauncher.Services;

public interface IImageCacheService
{
    BitmapImage? TryGetCached(string? url);
    Task<BitmapImage?> GetAsync(string? url, System.Net.Http.HttpClient http, int decodeWidth = 128, CancellationToken ct = default);
    void ClearMemory();
    long DiskCacheSize();
}

public sealed class ImageCacheServiceAdapter : IImageCacheService
{
    public BitmapImage? TryGetCached(string? url) => ImageCacheService.TryGetCached(url);
    public Task<BitmapImage?> GetAsync(string? url, System.Net.Http.HttpClient http, int decodeWidth = 128, CancellationToken ct = default)
        => ImageCacheService.GetAsync(url, http, decodeWidth, ct);
    public void ClearMemory() => ImageCacheService.ClearMemory();
    public long DiskCacheSize() => ImageCacheService.DiskCacheSize();
}
