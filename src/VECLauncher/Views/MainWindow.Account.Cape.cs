using IOPath = System.IO.Path;
using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows;
using System.Threading;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Views;

public partial class MainWindow
{
    private sealed class CapeDisplayItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? PreviewUrl { get; set; }
        public byte[]? RawBytes { get; set; }
        public ImageSource? PreviewImage { get; set; }
        public bool IsEquipped { get; set; }
    }

    private int _capeRefreshSeq = 0;

    public async Task RefreshCapesGridUIAsync(MinecraftAccount? acc)
    {
        if (CapesWrapPanel == null) return;
        if (acc == null)
        {
            CapesWrapPanel.Children.Clear();
            return;
        }

        var seq = Interlocked.Increment(ref _capeRefreshSeq);

        var items = new List<CapeDisplayItem>();

        var isNoCape = _currentCapeRawBytes == null || _currentCapeRawBytes.Length == 0;
        items.Add(new CapeDisplayItem
        {
            Id = "none",
            Name = "Без плаща",
            Description = "Снять плащ с персонажа.",
            IsEquipped = isNoCape
        });

        if (acc.IsVec)
        {
            try
            {
                var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233")) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
                var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{srvUrl}/Account/GetWardrobeState?username={Uri.EscapeDataString(acc.Username)}");
                if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
                req.Headers.Add("X-Username", acc.Username);
                using var resp = await App.Http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("skinModel", out var mProp) && mProp.ValueKind == JsonValueKind.String)
                    {
                        var serverModel = mProp.GetString();
                        if (!string.IsNullOrEmpty(serverModel))
                        {
                            _currentSkinModel = serverModel.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic";
                            acc.SkinModel = _currentSkinModel;
                            var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
                            Dispatcher.Invoke(() =>
                            {
                                if (ToggleModelSwitch != null) ToggleModelSwitch.IsChecked = isSlim;
                                if (TxtToggleModelBadge != null) TxtToggleModelBadge.Text = isSlim ? "Slim" : "Classic";
                            });
                        }
                    }

                    if (root.TryGetProperty("hasMinecraftLicense", out var licProp))
                    {
                        acc.HasMinecraftLicense = licProp.GetBoolean();
                        Dispatcher.Invoke(() =>
                        {
                            if (TxtLicenseBadge != null)
                            {
                                TxtLicenseBadge.Text = "Mojang";
                                TxtLicenseBadge.Visibility = acc.HasMinecraftLicense ? Visibility.Visible : Visibility.Collapsed;
                            }
                        });
                    }
                    if (root.TryGetProperty("microsoftGamertag", out var tagProp) && tagProp.ValueKind == JsonValueKind.String)
                    {
                        acc.MicrosoftGamertag = tagProp.GetString();
                    }

                    var equippedId = root.TryGetProperty("equippedCapeId", out var eqProp) ? eqProp.GetString() : null;

                    if (string.IsNullOrEmpty(equippedId) || equippedId == "none")
                    {
                        if (_currentCapeRawBytes != null)
                        {
                            _currentCapeRawBytes = null;
                            Dispatcher.Invoke(() => _gltfSkinModel.UpdateCapeTexture(null));
                        }
                    }
                    else if (_currentCapeRawBytes == null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var cUrl = $"{srvUrl}/api/cape/{Uri.EscapeDataString(acc.Username)}.png?t={DateTime.UtcNow.Ticks}";
                                using var cResp = await App.Http.GetAsync(cUrl);
                                if (cResp.IsSuccessStatusCode)
                                {
                                    var cBytes = await cResp.Content.ReadAsByteArrayAsync();
                                    _currentCapeRawBytes = cBytes;
                                    Dispatcher.Invoke(() => _gltfSkinModel.UpdateCapeTexture(cBytes));
                                }
                            }
                            catch { }
                        });
                    }

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
                                items.Add(new CapeDisplayItem
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
                                items.Add(new CapeDisplayItem
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
                Log.Warn($"Wardrobe fetch failed: {ex.Message}");
            }
        }

        if (!acc.IsVec && acc.Type == AccountType.Microsoft && !string.IsNullOrEmpty(acc.AccessToken))
        {
            try
            {
                using var mReq = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
                mReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", acc.AccessToken);
                using var mResp = await App.Http.SendAsync(mReq);
                if (mResp.IsSuccessStatusCode)
                {
                    var mJson = await mResp.Content.ReadAsStringAsync();
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
                                items.Add(new CapeDisplayItem
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
                Log.Warn($"Mojang capes fetch failed: {ex.Message}");
            }
        }

        if (items.Count <= 1)
        {
            var fallback = CapeService.GetAllCapes(acc, _currentCapeRawBytes);
            foreach (var fb in fallback)
            {
                if (fb.Id == "none") continue;
                if (!items.Any(i => i.Id.Equals(fb.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new CapeDisplayItem
                    {
                        Id = fb.Id,
                        Name = fb.Name,
                        Description = fb.Description,
                        IsEquipped = fb.IsEquipped,
                        RawBytes = fb.RawTextureBytes,
                        PreviewImage = fb.PreviewImage
                    });
                }
            }
        }

        if (seq != _capeRefreshSeq) return;

        var distinctItems = items.DistinctBy(i => i.Id).ToList();

        Dispatcher.Invoke(() =>
        {
            if (seq != _capeRefreshSeq || CapesWrapPanel == null) return;
            CapesWrapPanel.Children.Clear();
            foreach (var item in distinctItems)
            {
                var card = BuildCapeCard(acc, item);
                CapesWrapPanel.Children.Add(card);
            }
        });
    }

    private Border BuildCapeCard(MinecraftAccount acc, CapeDisplayItem item)
    {
        var cardBackground = new SolidColorBrush(item.IsEquipped ? Color.FromRgb(0x27, 0x2A, 0x34) : Color.FromRgb(0x23, 0x25, 0x2D));
        var cardBorder = new SolidColorBrush(item.IsEquipped ? Color.FromRgb(0x55, 0x5C, 0x70) : Color.FromRgb(0x32, 0x36, 0x42));

        var border = new Border
        {
            Tag = item,
            Width = 140,
            Height = 150,
            Background = cardBackground,
            BorderBrush = cardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(8, 10, 8, 8),
            Cursor = Cursors.Hand
        };

        border.MouseEnter += (s, e) =>
        {
            if (border.Tag is CapeDisplayItem cd && !cd.IsEquipped)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x37));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x4A, 0x5A));
            }
        };
        border.MouseLeave += (s, e) =>
        {
            if (border.Tag is CapeDisplayItem cd && !cd.IsEquipped)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x23, 0x25, 0x2D));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x36, 0x42));
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var previewContainer = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (item.Id == "none")
        {
            var dashedFrame = new Rectangle
            {
                Width = 44,
                Height = 70,
                Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x72)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = Brushes.Transparent
            };
            previewContainer.Child = dashedFrame;
        }
        else
        {
            var img = new Image
            {
                Width = 44,
                Height = 70,
                Stretch = Stretch.None
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);

            if (item.PreviewImage != null)
            {
                img.Source = item.PreviewImage;
            }
            else if (item.RawBytes != null)
            {
                img.Source = CapeService.CreateCapePreview(item.RawBytes);
            }
            else if (!string.IsNullOrEmpty(item.PreviewUrl))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var resp = await App.Http.GetAsync(item.PreviewUrl);
                        if (resp.IsSuccessStatusCode)
                        {
                            var b = await resp.Content.ReadAsByteArrayAsync();
                            var prev = CapeService.CreateCapePreview(b);
                            if (prev != null)
                            {
                                Dispatcher.Invoke(() => img.Source = prev);
                            }
                        }
                    }
                    catch { }
                });
            }

            previewContainer.Child = img;
        }

        Grid.SetRow(previewContainer, 0);
        grid.Children.Add(previewContainer);

        var footerBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x31, 0x3D)),
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 6, 0, 0)
        };

        var footerStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var radioBorder = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderBrush = new SolidColorBrush(item.IsEquipped ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x6B, 0x72, 0x80)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1D)),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (item.IsEquipped)
        {
            radioBorder.Child = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        footerStack.Children.Add(radioBorder);

        var titleBlock = new TextBlock
        {
            Text = item.Name,
            FontSize = 12,
            FontWeight = item.IsEquipped ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(item.IsEquipped ? Colors.White : Color.FromRgb(0xD1, 0xD5, 0xDB)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 96,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = item.Description
        };

        footerStack.Children.Add(titleBlock);
        footerBorder.Child = footerStack;

        Grid.SetRow(footerBorder, 1);
        grid.Children.Add(footerBorder);

        border.Child = grid;

        border.MouseLeftButtonUp += (s, e) =>
        {
            _ = EquipCapeByIdAsync(acc, item);
        };

        return border;
    }

    private void SelectCapeInUI(string targetCapeId)
    {
        if (CapesWrapPanel == null) return;
        foreach (var child in CapesWrapPanel.Children)
        {
            if (child is Border b && b.Tag is CapeDisplayItem cd)
            {
                var isTarget = cd.Id.Equals(targetCapeId, StringComparison.OrdinalIgnoreCase);
                cd.IsEquipped = isTarget;
                b.Background = new SolidColorBrush(isTarget ? Color.FromRgb(0x27, 0x2A, 0x34) : Color.FromRgb(0x23, 0x25, 0x2D));
                b.BorderBrush = new SolidColorBrush(isTarget ? Color.FromRgb(0x55, 0x5C, 0x70) : Color.FromRgb(0x32, 0x36, 0x42));

                if (b.Child is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border footerBorder && footerBorder.Child is StackPanel footerStack)
                {
                    if (footerStack.Children.Count > 0 && footerStack.Children[0] is Border radioBorder)
                    {
                        radioBorder.BorderBrush = new SolidColorBrush(isTarget ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x6B, 0x72, 0x80));
                        radioBorder.Child = isTarget ? new Ellipse
                        {
                            Width = 6,
                            Height = 6,
                            Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        } : null;
                    }
                    if (footerStack.Children.Count > 1 && footerStack.Children[1] is TextBlock titleBlock)
                    {
                        titleBlock.FontWeight = isTarget ? FontWeights.SemiBold : FontWeights.Normal;
                        titleBlock.Foreground = new SolidColorBrush(isTarget ? Colors.White : Color.FromRgb(0xD1, 0xD5, 0xDB));
                    }
                }
            }
        }
    }

    private async Task EquipCapeByIdAsync(MinecraftAccount acc, CapeDisplayItem item)
    {
        SelectCapeInUI(item.Id);
        try
        {
            var capePath = SkinService.AccountCapePath(acc.Username);
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233")) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
            var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

            if (item.Id == "none")
            {
                _currentCapeRawBytes = null;
                _gltfSkinModel.UpdateCapeTexture(null);
                if (File.Exists(capePath))
                {
                    _isInternalSkinWrite = true;
                    try { lock (_skinFileLock) { File.Delete(capePath); } }
                    finally { _isInternalSkinWrite = false; }
                }

                if (acc.IsVec)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/RemoveCape");
                    if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
                    req.Headers.Add("X-Username", acc.Username);
                    await App.Http.SendAsync(req);
                }

                _toast.Show("Гардероб", "Плащ снят.", NotificationType.Info);
                return;
            }

            byte[]? capeBytes = item.RawBytes;

            if (capeBytes == null && !string.IsNullOrEmpty(item.PreviewUrl))
            {
                using var resp = await App.Http.GetAsync(item.PreviewUrl);
                if (resp.IsSuccessStatusCode)
                {
                    capeBytes = await resp.Content.ReadAsByteArrayAsync();
                }
            }


            if (capeBytes != null && capeBytes.Length > 0)
            {
                var dir = IOPath.GetDirectoryName(capePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _isInternalSkinWrite = true;
                try { lock (_skinFileLock) { File.WriteAllBytes(capePath, capeBytes); } }
                finally { _isInternalSkinWrite = false; }

                _currentCapeRawBytes = capeBytes;
                _gltfSkinModel.UpdateCapeTexture(capeBytes);

                if (acc.IsVec)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/EquipCape");
                    if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
                    req.Headers.Add("X-Username", acc.Username);
                    req.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("capeId", item.Id),
                        new KeyValuePair<string, string>("username", acc.Username)
                    });
                    await App.Http.SendAsync(req);
                }

                _toast.Show("Гардероб", $"Плащ «{item.Name}» успешно надет!", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Equip cape failed: {ex.Message}");
            _toast.Show("Ошибка", ex.Message, NotificationType.Error);
            _ = RefreshCapesGridUIAsync(acc);
        }
    }

    private void TxtCapePromoCode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnRedeemCapePromo_Click(sender, e);
        }
    }

    private async void BtnRedeemCapePromo_Click(object sender, RoutedEventArgs e)
    {
        var code = TxtCapePromoCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            TxtCapePromoStatus.Text = "Введите промокод.";
            TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TxtCapePromoStatus.Visibility = Visibility.Visible;
            return;
        }

        var acc = _account ?? Vm.Accounts.GetActive();
        if (acc == null)
        {
            TxtCapePromoStatus.Text = "Сначала войдите в аккаунт.";
            TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TxtCapePromoStatus.Visibility = Visibility.Visible;
            return;
        }

        BtnRedeemCapePromo.IsEnabled = false;
        TxtCapePromoStatus.Text = "Проверка промокода...";
        TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA2, 0xB0));
        TxtCapePromoStatus.Visibility = Visibility.Visible;

        try
        {
            var srvUrl = (!string.IsNullOrEmpty(acc.ServerUrl) && !acc.ServerUrl.Contains("95.59.233")) ? acc.ServerUrl : await VecAuthService.GetActiveServerUrlAsync();
            var token = !string.IsNullOrEmpty(acc.SessionKey) ? acc.SessionKey : acc.AccessToken;

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{srvUrl}/Account/RedeemCapeCode");
            if (!string.IsNullOrEmpty(token)) req.Headers.Add("X-Session-Key", token);
            var jsonPayload = JsonSerializer.Serialize(new { code = code });
            req.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            using var resp = await App.Http.SendAsync(req);
            var respJson = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

            if (success)
            {
                TxtCapePromoStatus.Text = msg;
                TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                TxtCapePromoCode.Clear();
                _toast.Show("Промокод активирован", msg, NotificationType.Success);
                await RefreshCapesGridUIAsync(acc);
            }
            else
            {
                TxtCapePromoStatus.Text = !string.IsNullOrEmpty(msg) ? msg : "Неверный промокод.";
                TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                _toast.Show("Ошибка промокода", TxtCapePromoStatus.Text, NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            TxtCapePromoStatus.Text = $"Ошибка: {ex.Message}";
            TxtCapePromoStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            _toast.Show("Ошибка", ex.Message, NotificationType.Error);
        }
        finally
        {
            BtnRedeemCapePromo.IsEnabled = true;
        }
    }
}
