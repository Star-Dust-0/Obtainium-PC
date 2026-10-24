using System.Text.Json;
using System.Text.Json.Serialization;

namespace ObtainiumPC.Models;

/// <summary>
/// A tracked application. Mirrors Obtainium's lib/models/app.dart field-for-field
/// and serializes with the exact same JSON keys so an Obtainium export file can be
/// imported losslessly. Android-specific fields are preserved verbatim, not dropped.
/// Timestamps are microseconds since Unix epoch, as in the original schema.
/// </summary>
public sealed class TrackedApp
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("installedVersion")] public string? InstalledVersion { get; set; }
    [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }

    // The original schema stores asset lists and settings as JSON-encoded strings
    // inside the JSON document. Kept that way for byte-level import compatibility.
    [JsonPropertyName("apkUrls")] public string AssetUrlsJson { get; set; } = "[]";
    [JsonPropertyName("otherAssetUrls")] public string OtherAssetUrlsJson { get; set; } = "[]";
    [JsonPropertyName("preferredApkIndex")] public int PreferredAssetIndex { get; set; }
    [JsonPropertyName("additionalSettings")] public string AdditionalSettingsJson { get; set; } = "{}";

    [JsonPropertyName("lastUpdateCheck")] public long? LastUpdateCheckUs { get; set; }
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    [JsonPropertyName("disabled")] public bool Disabled { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("releaseDate")] public long? ReleaseDateUs { get; set; }
    [JsonPropertyName("changeLog")] public string? ChangeLog { get; set; }
    [JsonPropertyName("releaseUrl")] public string? ReleaseUrl { get; set; }
    [JsonPropertyName("overrideSource")] public string? OverrideSource { get; set; }
    [JsonPropertyName("allowIdChange")] public bool AllowIdChange { get; set; }
    [JsonPropertyName("pendingRepoRenameUrl")] public string? PendingRepoRenameUrl { get; set; }
    [JsonPropertyName("apkSizeBytes")] public long? AssetSizeBytes { get; set; }
    [JsonPropertyName("latestVersionChangedAt")] public long? LatestVersionChangedAtUs { get; set; }
    [JsonPropertyName("consecutiveFailedChecks")] public int ConsecutiveFailedChecks { get; set; }
    [JsonPropertyName("isArchived")] public bool? IsArchived { get; set; }
    [JsonPropertyName("newerReleasesFilteredOut")] public bool? NewerReleasesFilteredOut { get; set; }
    [JsonPropertyName("updateHealthEnabled")] public bool UpdateHealthEnabled { get; set; } = true;
    [JsonPropertyName("updateHealthDismissedVersion")] public string? UpdateHealthDismissedVersion { get; set; }

    // ---- Typed accessors over the JSON-encoded fields ----

    [JsonIgnore]
    public List<KeyValuePair<string, string>> AssetUrls
    {
        get => DecodePairs(AssetUrlsJson);
        set => AssetUrlsJson = EncodePairs(value);
    }

    [JsonIgnore]
    public List<KeyValuePair<string, string>> OtherAssetUrls
    {
        get => DecodePairs(OtherAssetUrlsJson);
        set => OtherAssetUrlsJson = EncodePairs(value);
    }

    [JsonIgnore]
    public Dictionary<string, JsonElement> AdditionalSettings
    {
        get
        {
            try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AdditionalSettingsJson) ?? new(); }
            catch { return new(); }
        }
        set => AdditionalSettingsJson = JsonSerializer.Serialize(value);
    }

    public string? GetSettingString(string key) =>
        AdditionalSettings.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public bool GetSettingBool(string key, bool fallback = false) =>
        AdditionalSettings.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;

    public void SetSetting(string key, object? value)
    {
        var s = AdditionalSettings;
        if (value is null) s.Remove(key);
        else s[key] = JsonSerializer.SerializeToElement(value);
        AdditionalSettings = s;
    }

    [JsonIgnore]
    public string? OverrideName
    {
        get { var n = GetSettingString("appName"); return string.IsNullOrWhiteSpace(n) ? null : n; }
    }

    [JsonIgnore]
    public string? OverrideAuthor
    {
        get { var a = GetSettingString("appAuthor"); return string.IsNullOrWhiteSpace(a) ? null : a; }
    }

    [JsonIgnore] public string FinalName => OverrideName ?? Name;
    [JsonIgnore] public string FinalAuthor => OverrideAuthor ?? Author;

    [JsonIgnore]
    public bool HasPendingRepoRename => !string.IsNullOrEmpty(PendingRepoRenameUrl);

    [JsonIgnore]
    public DateTimeOffset? LastUpdateCheck =>
        LastUpdateCheckUs is long us ? DateTimeOffset.FromUnixTimeMilliseconds(us / 1000) : null;

    [JsonIgnore]
    public DateTimeOffset? ReleaseDate =>
        ReleaseDateUs is long us ? DateTimeOffset.FromUnixTimeMilliseconds(us / 1000) : null;

    public static long ToMicroseconds(DateTimeOffset dto) => dto.ToUnixTimeMilliseconds() * 1000;

    private static List<KeyValuePair<string, string>> DecodePairs(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<string[]>>(json) ?? new();
            return raw.Where(p => p.Length >= 2).Select(p => new KeyValuePair<string, string>(p[0], p[1])).ToList();
        }
        catch { return new(); }
    }

    private static string EncodePairs(List<KeyValuePair<string, string>> pairs) =>
        JsonSerializer.Serialize(pairs.Select(p => new[] { p.Key, p.Value }).ToList());
}
