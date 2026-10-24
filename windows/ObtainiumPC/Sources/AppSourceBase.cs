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
    int? NewerReleasesFilteredOut);

public class ObtainiumSourceException : Exception
{
    public ObtainiumSourceException(string message) : base(message) { }
}

public sealed class InvalidUrlException : ObtainiumSourceException
{
    public InvalidUrlException(string source) : base($"Invalid URL for source {source}") { }
}

public sealed class NoReleasesException : ObtainiumSourceException
{
    public NoReleasesException() : base("No releases found") { }
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

    public abstract string SourceSpecificStandardizeUrl(string url, bool forSelection = false);

    public abstract Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default);

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
