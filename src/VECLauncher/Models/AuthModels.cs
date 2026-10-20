using System.Text.Json.Serialization;

namespace VECLauncher.Models;

public enum AccountType
{
    Microsoft = 0,
    Offline = 1,
    Vms = 2,
    Vec = 2
}

public sealed class MinecraftAccount
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("xuid")] public string? Xuid { get; set; }
    [JsonPropertyName("type")] public AccountType Type { get; set; } = AccountType.Microsoft;
    [JsonPropertyName("serverUrl")] public string? ServerUrl { get; set; }
    [JsonPropertyName("skinUrl")] public string? SkinUrl { get; set; }
    [JsonPropertyName("capeUrl")] public string? CapeUrl { get; set; }
    [JsonPropertyName("skinModel")] public string SkinModel { get; set; } = "classic";
    [JsonPropertyName("hasMinecraftLicense")] public bool HasMinecraftLicense { get; set; }
    [JsonPropertyName("microsoftGamertag")] public string? MicrosoftGamertag { get; set; }
    [JsonPropertyName("microsoftUuid")] public string? MicrosoftUuid { get; set; }
    [JsonPropertyName("sessionKey")] public string? SessionKey { get; set; }

    [JsonIgnore] public bool IsOffline => Type == AccountType.Offline;
    [JsonIgnore] public bool IsVms => Type == AccountType.Vms || Type == AccountType.Vec;
    [JsonIgnore] public bool IsVec => IsVms;
    [JsonIgnore] public string UserType => (IsOffline || IsVms) ? "legacy" : "msa";
    [JsonIgnore]
    public string DashedUuid
    {
        get
        {
            var u = Uuid.Replace("-", "");
            if (u.Length != 32) return Uuid;
            return $"{u[..8]}-{u.Substring(8, 4)}-{u.Substring(12, 4)}-{u.Substring(16, 4)}-{u.Substring(20)}";
        }
    }
}
