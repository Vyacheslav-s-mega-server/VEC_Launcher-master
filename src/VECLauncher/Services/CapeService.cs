using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class CapeInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? PreviewUrl { get; set; }
    public byte[]? RawBytes { get; set; }
    public bool IsEquipped { get; set; }
}

public sealed class WardrobeStateResult
{
    public string? SkinModel { get; set; }
    public bool HasMinecraftLicense { get; set; }
    public string? MicrosoftGamertag { get; set; }
    public string? EquippedCapeId { get; set; }
    public List<CapeInfo> Capes { get; set; } = new();
}

public interface ICapeService
{
    Task<WardrobeStateResult> GetWardrobeStateAsync(MinecraftAccount acc, CancellationToken ct = default);
    Task<byte[]?> DownloadCapeTextureAsync(string url, CancellationToken ct = default);
    Task<bool> EquipCapeAsync(MinecraftAccount acc, string capeId, CancellationToken ct = default);
    Task<bool> RemoveCapeAsync(MinecraftAccount acc, CancellationToken ct = default);
    Task<(bool Success, string Message)> RedeemCapePromoAsync(MinecraftAccount acc, string promoCode, CancellationToken ct = default);
}

public class CapeService : ICapeService
{
    private readonly HttpClient _http;

    public CapeService(HttpClient http)
    {
        _http = http;
    }

    public async Task<WardrobeStateResult> GetWardrobeStateAsync(MinecraftAccount acc, CancellationToken ct = default)
    {
        var result = new WardrobeStateResult();
        var items = new List<CapeInfo>();

        items.Add(new CapeInfo
        {
            Id = "none",
            Name = "Без плаща",
            Description = "Снять плащ с персонажа.",
            IsEquipped = true
        });

        if (acc.IsVec && !string.IsNullOrWhiteSpace(acc.Username))
        {
            try
            {
                var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233"))
                    ? acc.ServerUrl
                    : await VecAuthService.GetActiveServerUrlAsync();
                var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

                using var req = new HttpRequestMessage(HttpMethod.Get, $"{srvUrl}/Account/GetWardrobeState?username={Uri.EscapeDataString(acc.Username)}");
                if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
                req.Headers.Add("X-Username", acc.Username);

                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("skinModel", out var mProp) && mProp.ValueKind == JsonValueKind.String)
                    {
                        result.SkinModel = mProp.GetString();
                    }

                    if (root.TryGetProperty("hasMinecraftLicense", out var licProp))
                    {
                        result.HasMinecraftLicense = licProp.GetBoolean();
                    }

                    if (root.TryGetProperty("microsoftGamertag", out var tagProp) && tagProp.ValueKind == JsonValueKind.String)
                    {
                        result.MicrosoftGamertag = tagProp.GetString();
                    }

                    var equippedId = root.TryGetProperty("equippedCapeId", out var eqProp) ? eqProp.GetString() : null;
                    result.EquippedCapeId = equippedId;

                    if (!string.IsNullOrEmpty(equippedId) && equippedId != "none")
                    {
                        var noneItem = items.FirstOrDefault(i => i.Id == "none");
                        if (noneItem != null) noneItem.IsEquipped = false;
                    }

                    if (root.TryGetProperty("availableCapes", out var capesArr) && capesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in capesArr.EnumerateArray())
                        {
                            var id = el.GetProperty("id").GetString() ?? "";
                            var name = el.GetProperty("name").GetString() ?? id;
                            var desc = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                            var preview = el.TryGetProperty("previewUrl", out var p) ? p.GetString() ?? "" : "";
                            var isEq = el.TryGetProperty("isEquipped", out var eq) && eq.GetBoolean();

                            if (!items.Any(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                            {
                                items.Add(new CapeInfo
                                {
                                    Id = id,
                                    Name = name,
                                    Description = desc,
                                    PreviewUrl = preview.StartsWith("/") ? $"{srvUrl}{preview}" : preview,
                                    IsEquipped = isEq
                                });
                            }
                        }
                    }

                    if (root.TryGetProperty("mojangCapes", out var mojangArr) && mojangArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in mojangArr.EnumerateArray())
                        {
                            var mId = el.GetProperty("id").GetString() ?? "";
                            var alias = el.TryGetProperty("alias", out var a) ? a.GetString() : null;
                            var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            var fullId = $"mojang_{mId}";

                            if (!items.Any(i => i.Id.Equals(fullId, StringComparison.OrdinalIgnoreCase)))
                            {
                                var resolvedUrl = url.StartsWith("/") ? $"{srvUrl}{url}" : (string.IsNullOrEmpty(url) ? $"{srvUrl}/img/capes/mojang/{mId}.png" : url);
                                items.Add(new CapeInfo
                                {
                                    Id = fullId,
                                    Name = !string.IsNullOrEmpty(alias) ? alias : "Плащ Mojang",
                                    Description = "Официальный плащ учетной записи Mojang / Microsoft",
                                    PreviewUrl = resolvedUrl,
                                    IsEquipped = equippedId == fullId
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"CapeService: Wardrobe fetch failed: {ex.Message}");
            }
        }
        else if (acc.Type == AccountType.Microsoft && !string.IsNullOrEmpty(acc.AccessToken))
        {
            try
            {
                using var mReq = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
                mReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", acc.AccessToken);
                using var mResp = await _http.SendAsync(mReq, ct);
                if (mResp.IsSuccessStatusCode)
                {
                    var mJson = await mResp.Content.ReadAsStringAsync(ct);
                    using var mDoc = JsonDocument.Parse(mJson);
                    if (mDoc.RootElement.TryGetProperty("capes", out var capesArr) && capesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in capesArr.EnumerateArray())
                        {
                            var cId = c.TryGetProperty("id", out var cid) ? cid.GetString() : "";
                            var cAlias = c.TryGetProperty("alias", out var ca) ? ca.GetString() : "";
                            var cUrl = c.TryGetProperty("url", out var cu) ? cu.GetString() : "";
                            var cState = c.TryGetProperty("state", out var cs) ? cs.GetString() : "";
                            var isEq = cState == "ACTIVE";
                            if (!string.IsNullOrEmpty(cId))
                            {
                                items.Add(new CapeInfo
                                {
                                    Id = $"mojang_{cId}",
                                    Name = !string.IsNullOrEmpty(cAlias) ? cAlias : "Плащ Mojang",
                                    Description = "Официальный плащ учетной записи Mojang / Microsoft",
                                    PreviewUrl = cUrl,
                                    IsEquipped = isEq
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"CapeService: Mojang capes fetch failed: {ex.Message}");
            }
        }

        result.Capes = items.DistinctBy(i => i.Id).ToList();
        return result;
    }

    public async Task<byte[]?> DownloadCapeTextureAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: DownloadCapeTexture failed for {url}: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> EquipCapeAsync(MinecraftAccount acc, string capeId, CancellationToken ct = default)
    {
        if (!acc.IsVec) return true;

        try
        {
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233"))
                ? acc.ServerUrl
                : await VecAuthService.GetActiveServerUrlAsync();
            var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/EquipCape");
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
            req.Headers.Add("X-Username", acc.Username);
            req.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("capeId", capeId),
                new KeyValuePair<string, string>("username", acc.Username)
            });

            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: EquipCape failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveCapeAsync(MinecraftAccount acc, CancellationToken ct = default)
    {
        if (!acc.IsVec) return true;

        try
        {
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233"))
                ? acc.ServerUrl
                : await VecAuthService.GetActiveServerUrlAsync();
            var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/RemoveCape");
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
            req.Headers.Add("X-Username", acc.Username);

            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: RemoveCape failed: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string Message)> RedeemCapePromoAsync(MinecraftAccount acc, string promoCode, CancellationToken ct = default)
    {
        try
        {
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233"))
                ? acc.ServerUrl
                : await VecAuthService.GetActiveServerUrlAsync();
            var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/RedeemCapeCode");
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
            var jsonPayload = JsonSerializer.Serialize(new { code = promoCode });
            req.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var respJson = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

            return (success, msg);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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
}