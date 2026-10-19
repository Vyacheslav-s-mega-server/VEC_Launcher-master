using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class SkinService
{
    private readonly HttpClient _http;

    public SkinService(HttpClient http) => _http = http;

    public async Task<byte[]?> TryDownloadAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"SkinService: failed to download image {url}: {ex.Message}");
            return null;
        }
    }

    public enum SkinModel { Classic, Slim }

    public async Task<bool> UploadSkinToVecServerAsync(string serverUrl, string username, byte[] skinBytes, SkinModel model, string? uuid = null, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(model == SkinModel.Slim ? "slim" : "classic"), "model");

            var fileContent = new ByteArrayContent(skinBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", $"{username}.png");

            Log.Info($"SkinService: uploading skin '{username}' to VEC server ({cleanUrl}), model={model}, size={skinBytes.Length} bytes...");
            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/upload", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin '{username}' synced to VEC server. Response: {body}");
                return true;
            }
            else
            {
                Log.Warn($"SkinService: VEC server returned {(int)resp.StatusCode} for skin '{username}': {body}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to upload skin to VEC server ({serverUrl}): {ex.Message}");
            return false;
        }
    }

    public async Task UploadCapeToVecServerAsync(string serverUrl, string username, byte[] capeBytes, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");

            var fileContent = new ByteArrayContent(capeBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", $"{username}.png");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/cape/upload", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: cape '{username}' synced to VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to upload cape to VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task ResetCapeOnVecServerAsync(string serverUrl, string username, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/cape/reset", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: cape '{username}' reset on VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to reset cape on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task ResetSkinOnVecServerAsync(string serverUrl, string username, byte[]? defaultSkinBytes, string model, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(model), "model");

            if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
            {
                var fileContent = new ByteArrayContent(defaultSkinBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(fileContent, "file", $"{username}.png");
            }

            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/reset", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin '{username}' reset on VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to reset skin on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task UpdateSkinModelOnVecServerAsync(string serverUrl, string username, string model, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = string.IsNullOrWhiteSpace(serverUrl) ? VecAuthService.DefaultVecServerUrl : serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(model), "model");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/model", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin model '{username}' updated on VEC server: {model}.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to update skin model on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task<(byte[]? SkinBytes, byte[]? CapeBytes, string? Model)> SyncSkinFromServerAsync(MinecraftAccount acc, CancellationToken ct = default)
    {
        byte[]? skinBytes = null;
        byte[]? capeBytes = null;
        string? model = null;

        if (!acc.IsVec) return (null, null, null);

        try
        {
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233"))
                ? acc.ServerUrl
                : await VecAuthService.GetActiveServerUrlAsync();
            acc.ServerUrl = srvUrl;

            var skinUrl = $"{srvUrl}/api/skin/{Uri.EscapeDataString(acc.Username)}.png?t={DateTime.UtcNow.Ticks}";
            using var sResp = await _http.GetAsync(skinUrl, ct).ConfigureAwait(false);
            if (sResp.IsSuccessStatusCode)
            {
                skinBytes = await sResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var skinPath = AccountSkinPath(acc.Username);
                await SafeWriteAllBytesAsync(skinPath, skinBytes, ct).ConfigureAwait(false);
            }

            var capeUrl = $"{srvUrl}/api/cape/{Uri.EscapeDataString(acc.Username)}.png?t={DateTime.UtcNow.Ticks}";
            using var cResp = await _http.GetAsync(capeUrl, ct).ConfigureAwait(false);
            if (cResp.IsSuccessStatusCode)
            {
                capeBytes = await cResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var capePath = AccountCapePath(acc.Username);
                await SafeWriteAllBytesAsync(capePath, capeBytes, ct).ConfigureAwait(false);
            }
            else if (cResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var capePath = AccountCapePath(acc.Username);
                SafeDelete(capePath);
            }

            try
            {
                var metaUrl = $"{srvUrl}/api/skin/{Uri.EscapeDataString(acc.Username)}.json?t={DateTime.UtcNow.Ticks}";
                using var mResp = await _http.GetAsync(metaUrl, ct).ConfigureAwait(false);
                if (mResp.IsSuccessStatusCode)
                {
                    var metaJson = await mResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("model", out var mProp))
                    {
                        var m = mProp.GetString();
                        if (!string.IsNullOrEmpty(m)) model = m;
                    }
                }
            }
            catch { }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"SkinService: SyncSkinFromServerAsync failed for {acc.Username}: {ex.Message}");
        }

        return (skinBytes, capeBytes, model);
    }

    public static void ValidateSkinPng(byte[] data)
    {
        if (data.Length < 24)
            throw new InvalidDataException("File too small - not a PNG.");

        ReadOnlySpan<byte> sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!data.AsSpan(0, 8).SequenceEqual(sig))
            throw new InvalidDataException("Skin must be in PNG format.");

        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];

        var valid = (width == 64 && (height == 64 || height == 32)) ||
                    (width == 128 && height == 128) ||
                    (width == 256 && height == 256);

        if (!valid)
            throw new InvalidDataException(
                $"Invalid skin size: {width}x{height}. Required: 64x64 (or 64x32).");

        if (data.Length > 512 * 1024)
            throw new InvalidDataException("Skin file too large (max 512 KB).");
    }

    public static System.Windows.Media.Imaging.BitmapSource? CreateCrispHeadAvatar(byte[] skinBytes, int targetSize = 64)
    {
        try
        {
            using var ms = new MemoryStream(skinBytes);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int w = conv.PixelWidth;
            int h = conv.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[h * stride];
            conv.CopyPixels(pixels, stride, 0);

            var scale = Math.Max(1, w / 64);
            int headX = 8 * scale;
            int headY = 8 * scale;
            int headSize = 8 * scale;
            int hatX = 40 * scale;
            int hatY = 8 * scale;

            int outStride = targetSize * 4;
            var outPixels = new byte[targetSize * outStride];

            for (int dy = 0; dy < targetSize; dy++)
            {
                int srcY = headY + (dy * headSize / targetSize);
                int hatSrcY = hatY + (dy * headSize / targetSize);

                for (int dx = 0; dx < targetSize; dx++)
                {
                    int srcX = headX + (dx * headSize / targetSize);
                    int hatSrcX = hatX + (dx * headSize / targetSize);

                    int baseIdx = srcY * stride + srcX * 4;
                    byte b = pixels[baseIdx];
                    byte g = pixels[baseIdx + 1];
                    byte r = pixels[baseIdx + 2];
                    byte a = pixels[baseIdx + 3];

                    if (h >= 64 * scale)
                    {
                        int hatIdx = hatSrcY * stride + hatSrcX * 4;
                        byte hatA = pixels[hatIdx + 3];
                        if (hatA > 20)
                        {
                            double alpha = hatA / 255.0;
                            b = (byte)(pixels[hatIdx] * alpha + b * (1.0 - alpha));
                            g = (byte)(pixels[hatIdx + 1] * alpha + g * (1.0 - alpha));
                            r = (byte)(pixels[hatIdx + 2] * alpha + r * (1.0 - alpha));
                            a = 255;
                        }
                    }

                    int outIdx = dy * outStride + dx * 4;
                    outPixels[outIdx] = b;
                    outPixels[outIdx + 1] = g;
                    outPixels[outIdx + 2] = r;
                    outPixels[outIdx + 3] = a;
                }
            }

            var result = System.Windows.Media.Imaging.BitmapSource.Create(
                targetSize, targetSize, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                outPixels, outStride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> RedeemPromoCodeAsync(string serverUrl, string username, string code, CancellationToken ct = default)
    {
        var cleanUrl = serverUrl.TrimEnd('/');
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(username), "username");
        content.Add(new StringContent(code), "code");

        using var resp = await _http.PostAsync($"{cleanUrl}/api/promo/redeem", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Error: {body}");

        var json = JsonNode.Parse(body);
        return json?["message"]?.ToString() ?? "Done!";
    }

    public static string AccountSkinPath(string username) =>
        Path.Combine(LauncherPaths.Root, "skins", username + ".png");

    public static string AccountCapePath(string username) =>
        Path.Combine(LauncherPaths.Root, "capes", username + ".png");

    public static string? FindAccountSkin(string username)
    {
        var exact = AccountSkinPath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "skins");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindAccountCape(string username)
    {
        var exact = AccountCapePath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "capes");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task SafeWriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                await fs.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
    }

    public static async Task<byte[]?> SafeReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms, ct).ConfigureAwait(false);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }
        return null;
    }

    public static void SafeDelete(string path)
    {
        if (!File.Exists(path)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch { return; }
        }
    }
}