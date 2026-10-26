using System.Text.Json;
using System.Text.RegularExpressions;

namespace ObtainiumPC.Sources;

public sealed record AppNames(string Author, string Name);

/// Result of a latest-release lookup. Mirrors Obtainium's APKDetails.
public sealed record AssetDetails(
    string Version,
    List<KeyValuePair<string, string>> AssetUrls,
    AppNames Names,
    DateTimeOffset? ReleaseDate,
    string? ChangeLog,
    List<KeyValuePair<string, string>> AllAssetUrls,
    bool? IsArchived,
    int? NewerReleasesFilteredOut)
{
    /// Optional context fields some sources resolve (APKMirror): release page,
    /// real package id and size. Init-only so positional construction is unaffected.
    public string? ReleaseUrl { get; init; }
    public string? AppId { get; init; }
    public long? ApkSizeBytes { get; init; }
}

public class ObtainiumSourceException : Exception
{
    public ObtainiumSourceException(string message) : base(message) { }
}

public sealed class ObtainiumException : ObtainiumSourceException
{
    public ObtainiumException(string message) : base(message) { }
}

public sealed class InvalidUrlException : ObtainiumSourceException
{
    public InvalidUrlException(string source) : base($"Invalid URL for source {source}") { }
}

public sealed class AndroidSourceNotSupportedException : ObtainiumSourceException
{
    public const string Text = "Android apps and Android app stores are not supported. Obtainium-PC tracks Windows apps only.";
    public AndroidSourceNotSupportedException() : base(Text) { }
}

public sealed class NoReleasesException : ObtainiumSourceException
{
    public NoReleasesException() : base("No releases found") { }
}

public sealed class NoAppAssetException : ObtainiumSourceException
{
    /// Shown (and localized by SourceErrorText) when releases exist but none has a
    /// Windows installer asset. The English literal is also the translation key.
    public const string NoWindowsInstallerMessage =
        "Releases were found, but none has a Windows installer asset (.exe, .msi, .msix, .appx, .appinstaller). " +
        "Turn on track-only for this app to follow its version without installing.";

    public NoAppAssetException() : base("No installable asset found for this app") { }
    public NoAppAssetException(string message) : base(message) { }
    public static NoAppAssetException NoWindowsInstaller() => new(NoWindowsInstallerMessage);
}

public sealed class RepositoryRenamedException : ObtainiumSourceException
{
    public string OldUrl { get; }
    public string NewUrl { get; }
    public RepositoryRenamedException(string oldUrl, string newUrl)
        : base($"Repository moved: {oldUrl} -> {newUrl}") { OldUrl = oldUrl; NewUrl = newUrl; }
}

public sealed class ObtainiumHttpException : ObtainiumSourceException
{
    public int StatusCode { get; }
    public ObtainiumHttpException(int statusCode, string url)
        : base($"HTTP {statusCode} for {url}") { StatusCode = statusCode; }
}

/// <summary>
/// Base class for app sources. Ports the shared helpers from
/// lib/app_sources/app_source.dart and lib/utils/string_utils.dart.
/// </summary>
public abstract class AppSourceBase
{
    public abstract string Name { get; }
    public abstract IReadOnlyList<string> Hosts { get; }

    /// False for sources whose artifacts are Android packages: tracking and
    /// downloading work, but Windows cannot install them (track-only).
    public virtual bool SupportsInstall => true;

    /// Upstream: neverAutoSelect - the user must pick the asset every time.
    public virtual bool NeverAutoSelect => false;

    /// Upstream: naiveStandardVersionDetection.
    public virtual bool NaiveStandardVersionDetection => false;

    public abstract string SourceSpecificStandardizeUrl(string url, bool forSelection = false);

    /// Per-source additional form items (e.g. GitHub's sort method). Rows of items,
    /// matching the original's List<List<GeneratedFormItem>>.
    public virtual IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems =>
        Array.Empty<IReadOnlyList<Forms.OptionFormItem>>();

    /// Original: suppressStandardVersionExtraction (SourceForge sets it true -
    /// the source extracts versions itself during release discovery).
    public virtual bool SuppressStandardVersionExtraction => false;

    /// Original: showReleaseDateAsVersionToggle (GitHub sets it true).
    public virtual bool ShowReleaseDateAsVersionToggle => false;

    /// Original: allowIncludeZips / allowIncludeTarballs.
    public virtual bool AllowIncludeZips => false;
    public virtual bool AllowIncludeTarballs => false;

    /// Original: versionDetectionDisallowed.
    public virtual bool VersionDetectionDisallowed => false;


    /// Original: excludeCommonSettingKeys.
    public virtual IReadOnlyList<string> ExcludeCommonSettingKeys => Array.Empty<string>();

    public abstract Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default);

    /// Original: changeLogPageFromStandardUrl (null = no separate changelog page).
    public virtual string? ChangeLogPageFromStandardUrl(string standardUrl) => null;

    /// Original: getRequestHeaders(forAPKDownload: true). Extra headers sent
    /// with the actual asset download (e.g. Referer requirements).
    public virtual IReadOnlyDictionary<string, string>? GetDownloadRequestHeaders(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings) => null;

    /// Original: canSearch. Whether the source supports app search by name.
    public virtual bool CanSearch => false;

    /// <summary>lib/app_sources/app_source.dart includeAdditionalOptsInMainSearch:
    /// the Add App search flow shows this source's per-search settings dialog.</summary>
    public virtual bool IncludeAdditionalOptsInMainSearch => false;

    /// <summary>Integer query setting from the per-search settings dialog.</summary>
    protected static int GetQueryInt(IReadOnlyDictionary<string, JsonElement> querySettings, string key)
    {
        if (!querySettings.TryGetValue(key, out var v)) return 0;
        try
        {
            return v.ValueKind == JsonValueKind.Number ? v.GetInt32()
                : int.TryParse(v.ToString(), out var n) ? n : 0;
        }
        catch { return 0; }
    }

    /// <summary>String query setting from the per-search settings dialog.</summary>
    protected static string? GetQueryString(IReadOnlyDictionary<string, JsonElement> querySettings, string key)
    {
        if (!querySettings.TryGetValue(key, out var v)) return null;
        var t = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    /// Original: search. Returns url -> [name, description]; null = unsupported.
    public virtual Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement> querySettings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(null);

    /// Original: assetUrlPrefetchModifier. Applied at download time, never stored.
    public virtual string AssetUrlPrefetchModifier(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings) => assetUrl;

    /// Async variant for sources that must resolve a signed URL at download
    /// time (itch.io). Defaults to the sync modifier.
    public virtual Task<string> AssetUrlPrefetchModifierAsync(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AssetUrlPrefetchModifier(assetUrl, standardUrl, additionalSettings));

    /// Original: isReleaseTooYoung from lib/utils/min_update_age.dart.
    protected static bool IsReleaseTooYoung(DateTimeOffset? releaseDate, int minAgeDays)
    {
        if (minAgeDays <= 0 || releaseDate == null) return false;
        return (DateTimeOffset.UtcNow - releaseDate.Value).TotalDays < minAgeDays;
    }

    /// Builds a regex alternation from hostnames, escaping dots.
    protected static string GetSourceRegex(IReadOnlyList<string> hosts) =>
        "(" + string.Join("|", hosts).Replace(".", "\\.") + ")";

    /// Standardize-by-regex helper shared by many sources.
    protected string StandardizeUrlWithRegex(string url, string subdomainPrefix, string pathPattern)
    {
        var re = new Regex("^https?://" + subdomainPrefix + GetSourceRegex(Hosts) + pathPattern,
            RegexOptions.IgnoreCase);
        var match = re.Match(url);
        if (!match.Success) throw new InvalidUrlException(Name);
        return match.Value;
    }

    protected static bool PassesRegExFilter(Regex regEx, string value, bool invert = false)
    {
        var matches = regEx.IsMatch(value);
        return invert ? !matches : matches;
    }

    protected static string? GetString(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    protected static bool GetBool(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;

    protected static int? GetInt(IReadOnlyDictionary<string, JsonElement> s, string key)
    {
        if (!s.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var p)) return p;
        return null;
    }
}
