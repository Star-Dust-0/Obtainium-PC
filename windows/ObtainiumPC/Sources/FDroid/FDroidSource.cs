using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ObtainiumPC.Sources.FDroid;

/// <summary>
/// Port of lib/app_sources/fdroid.dart (official repo). Track-only on
/// Windows: APKs are version/link-tracked and downloadable for manual
/// transfer, but cannot be installed. Android ABI nativecode filtering is
/// intentionally not ported (no ABIs on Windows); "Added on" dates still
/// come from the package page.
/// </summary>
public class FDroidSource : AppSourceBase
{
    private const int MaxChangeLogCodeUnits = 2048;
    private const string FDroidDataBaseUrl = "https://gitlab.com/fdroid/fdroiddata/-/raw/master/metadata";

    private readonly HttpClient _http;
    private readonly Dictionary<string, (FDroidPageMetadata Data, DateTimeOffset At)> _pageCache = new();
    private static readonly TimeSpan PageCacheDuration = TimeSpan.FromMinutes(10);

    public FDroidSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "F-Droid";
    public override IReadOnlyList<string> Hosts => new[] { "f-droid.org" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;

    /// API endpoint for the package JSON (IzzyOnDroid overrides both this and
    /// the APK URL prefix with its own hosts).
    public virtual string GetApiUrl(string appId) => $"https://f-droid.org/api/v1/packages/{appId}";
    public virtual string GetApkUrlPrefix(string appId) => $"https://f-droid.org/repo/{appId}";

    /// Only the official repo has fdroiddata metadata (author/changelog).
    public virtual bool IsOfficialHost => true;

    // lib/app_sources/fdroid.dart search: scrape search.f-droid.org package
    // headers; URLs standardized so results match stored app URLs.
    public override bool CanSearch => true;

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query, IReadOnlyDictionary<string, JsonElement> querySettings, CancellationToken ct = default)
    {
        var res = await _http.GetAsync(
            $"https://search.{Hosts[0]}/?q=" + Uri.EscapeDataString(query), ct);
        res.EnsureSuccessStatusCode();
        var doc = new HtmlDocument();
        doc.LoadHtml(await res.Content.ReadAsStringAsync(ct));
        var results = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var e in doc.DocumentNode.SelectNodes("//a[contains(@class,'package-header')]") ?? new HtmlNodeCollection(null))
        {
            var href = e.GetAttributeValue("href", null);
            if (href == null) continue;
            string url;
            try { url = SourceSpecificStandardizeUrl(href); }
            catch { continue; }
            var name = e.SelectSingleNode(".//*[contains(@class,'package-name')]")?.InnerText.Trim() ?? "";
            var desc = e.SelectSingleNode(".//*[contains(@class,'package-summary')]")?.InnerText.Trim() ?? "No description";
            results[url] = new List<string> { name, desc };
        }
        return results;
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var hostPattern = string.Join("|", Hosts.Select(h => Regex.Escape(h)));
        var matchB = Regex.Match(url, $"^https?://(www\\.)?({hostPattern})/+[^/]+/+packages/+[^/]+", RegexOptions.IgnoreCase);
        if (matchB.Success)
        {
            var lastSegment = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            url = $"https://{new Uri(matchB.Value).Host}/packages/{lastSegment}";
        }
        var matchA = Regex.Match(url, $"^https?://(www\\.)?({hostPattern})/+packages/+[^/]+", RegexOptions.IgnoreCase);
        if (!matchA.Success) throw new InvalidUrlException(Name);
        return matchA.Value;
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        var res = await _http.GetAsync(url, ct);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, url);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        using var doc = await GetJson(GetApiUrl(appId), cancellationToken);
        var details = ParsePackagesApiResponse(doc.RootElement, GetApkUrlPrefix(appId), standardUrl, additionalSettings);

        if (IsOfficialHost)
        {
            string? changeLog = details.ChangeLog;
            var author = details.Names.Author;
            try
            {
                var res = await _http.GetAsync($"{FDroidDataBaseUrl}/{appId}.yml", cancellationToken);
                if ((int)res.StatusCode == 200)
                {
                    var lines = (await res.Content.ReadAsStringAsync(cancellationToken)).Split('\n');
                    var authorLine = lines.FirstOrDefault(l => l.StartsWith("AuthorName: "));
                    if (authorLine != null) author = authorLine["AuthorName: ".Length..].Trim();
                    var changelogLine = lines.FirstOrDefault(l => l.StartsWith("Changelog: "));
                    if (changelogLine != null)
                    {
                        changeLog = changelogLine["Changelog: ".Length..].Trim();
                        // Fetch raw content for GitHub/GitLab blob links.
                        if ((changeLog.Contains("github.com") || changeLog.Contains("gitlab.com"))
                            && changeLog.Contains("/blob/"))
                        {
                            var raw = await _http.GetAsync(changeLog.Replace("/blob/", "/raw/"), cancellationToken);
                            if ((int)raw.StatusCode == 200)
                                changeLog = await raw.Content.ReadAsStringAsync(cancellationToken);
                        }
                    }
                }
            }
            catch { /* changelog/author enrichment is best-effort */ }

            if (changeLog is { Length: > MaxChangeLogCodeUnits })
                changeLog = changeLog[..MaxChangeLogCodeUnits] + "...";

            details = details with { ChangeLog = changeLog, Names = new AppNames(author, details.Names.Name) };
        }

        return details;
    }

    private AssetDetails ParsePackagesApiResponse(
        JsonElement response, string apkUrlPrefix, string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var autoSelectHighestVersionCode = SettingBool(additionalSettings, "autoSelectHighestVersionCode");
        var trySelectingSuggestedVersionCode = SettingBool(additionalSettings, "trySelectingSuggestedVersionCode");
        var filterVersionsByRegEx = SettingString(additionalSettings, "filterVersionsByRegEx");
        var apkFilterRegEx = SettingString(additionalSettings, "apkFilterRegEx");

        var packages = response.TryGetProperty("packages", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().ToList()
            : new List<JsonElement>();
        if (apkFilterRegEx != null)
        {
            var filter = new Regex(apkFilterRegEx);
            packages = packages.Where(rel =>
            {
                var code = rel.TryGetProperty("versionCode", out var vc) ? vc.ToString() : "";
                return filter.IsMatch($"{apkUrlPrefix}_{code}.apk");
            }).ToList();
        }
        if (packages.Count == 0) throw new NoReleasesException();

        int? suggestedVersionCode = null;
        if (response.TryGetProperty("suggestedVersionCode", out var svc) && svc.ValueKind == JsonValueKind.Number)
            suggestedVersionCode = svc.GetInt32();

        string? version = null;
        List<JsonElement> releaseChoices = new();

        if (trySelectingSuggestedVersionCode && suggestedVersionCode != null && filterVersionsByRegEx == null)
        {
            var suggested = packages.Where(e => GetVersionCode(e) == suggestedVersionCode).ToList();
            if (suggested.Count > 0)
            {
                releaseChoices = suggested;
                version = GetVersionName(suggested[0]);
            }
        }

        if (!string.IsNullOrEmpty(filterVersionsByRegEx))
        {
            version = null;
            releaseChoices = new List<JsonElement>();
            var versionFilter = new Regex(filterVersionsByRegEx);
            foreach (var rel in packages)
            {
                var name = GetVersionName(rel);
                if (name != null && versionFilter.IsMatch(name)) { version = name; break; }
            }
            if (string.IsNullOrEmpty(version)) throw new NoVersionException();
        }

        version ??= GetVersionName(packages[0]);
        if (string.IsNullOrEmpty(version)) throw new NoVersionException();

        if (releaseChoices.Count == 0)
            releaseChoices = packages.Where(e => GetVersionName(e) == version).ToList();

        if (releaseChoices.Count > 1)
        {
            if (autoSelectHighestVersionCode)
            {
                releaseChoices = new List<JsonElement> { releaseChoices[0] };
            }
            else if (trySelectingSuggestedVersionCode && suggestedVersionCode != null)
            {
                var suggested = releaseChoices.Where(e => GetVersionCode(e) == suggestedVersionCode).ToList();
                if (suggested.Count > 0) releaseChoices = suggested;
            }
        }
        if (releaseChoices.Count == 0) throw new NoReleasesException();

        if (SettingBool(additionalSettings, "useVersionCodeAsOSVersion"))
            version = GetVersionCode(releaseChoices[0])?.ToString() ?? version;

        var apkUrls = releaseChoices
            .Select(e => $"{apkUrlPrefix}_{GetVersionCode(e)}.apk")
            .Distinct()
            .ToList();

        return new AssetDetails(
            version,
            apkUrls.Select(u => new KeyValuePair<string, string>(u.Split('/').Last(), u)).ToList(),
            new AppNames(Name, new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last()),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    private static string? GetVersionName(JsonElement release) =>
        release.TryGetProperty("versionName", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetVersionCode(JsonElement release) =>
        release.TryGetProperty("versionCode", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static bool SettingBool(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.True;

    private static string? SettingString(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString())
            ? el.GetString() : null;
}

public sealed record FDroidPageMetadata(Dictionary<int, List<string>> Nativecodes, Dictionary<int, DateTimeOffset> AddedOn);

public sealed class NoVersionException : ObtainiumSourceException
{
    public NoVersionException() : base("No version found") { }
}
