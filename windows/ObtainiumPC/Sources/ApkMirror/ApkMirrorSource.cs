using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ObtainiumPC.Sources.ApkMirror;

/// <summary>
/// Port of lib/app_sources/apkmirror.dart: APKMirror tracking via the app_exists
/// API when a package id is known, else the listing RSS feed. APKMirror forbids
/// direct APK downloads (upstream PR #44), so this source is always track-only
/// with an empty asset list. Windows mappings: no current-app context (every
//  release counts as new), no cross-app batch prefetch, Obtainium version in the
/// user agent is the fallback constant.
/// </summary>
public sealed class ApkMirrorSource : AppSourceBase
{
    private const string FallbackVersion = "1.0.0";
    private const string ApiBasicAuth =
        "Basic YXBpLWFwa3VwZGF0ZXI6cm01cmNmcnVVakt5MDRzTXB5TVBKWFc4";
    private const string ApiUrl = "https://www.apkmirror.com/wp-json/apkm/v1/app_exists/";
    private static readonly TimeSpan ApiCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ApiAuthBlockDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan ApiTransientBlockDuration = TimeSpan.FromMinutes(5);

    private static readonly Regex PackagePattern = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    private sealed record ApiAppInfo(
        string Pname, bool Exists, string? AppName, string? AppLink,
        string? DeveloperName, string? Version, DateTimeOffset? ReleaseDate,
        string? WhatsNew, string? ReleaseLink);

    private readonly HttpClient _http;
    private readonly Dictionary<string, (ApiAppInfo Info, DateTimeOffset FetchedAt)> _apiCache = new();
    private DateTimeOffset? _apiBlockedUntil;

    public ApkMirrorSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public override string Name => "APKMirror";
    public override IReadOnlyList<string> Hosts => new[] { "apkmirror.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false; // enforceTrackOnly upstream
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release selection", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
            new Forms.OptionFormItem[] { new() { Key = "filterReleaseTitlesByRegEx", Label = "Filter release titles by regex", Kind = Forms.OptionFormItemKind.Text } },
            new Forms.OptionFormItem[] { new() { Key = "invertReleaseTitlesFilter", Label = "Invert regex (filter release titles)", Kind = Forms.OptionFormItemKind.Switch } },
        };

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, @"(www\.)?", @"/apk/[^/]+/[^/]+");

    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => $"{standardUrl}/#whatsnew";

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.TryAddWithoutValidation("User-Agent",
            $"Obtainium/{FallbackVersion} (+https://github.com/ImranR98/Obtainium)");
        if (ApiHostAllowed(new Uri(url)) && new Uri(url).AbsolutePath.StartsWith("/wp-json/apkm/v1/"))
            req.Headers.TryAddWithoutValidation("Authorization", ApiBasicAuth);
        return req;
    }

    private static bool ApiHostAllowed(Uri uri) =>
        uri.Host == "www.apkmirror.com" || uri.Host == "apkmirror.com";

    private int? ApiBlockedMinutes
    {
        get
        {
            if (_apiBlockedUntil is not { } until) return null;
            var now = DateTimeOffset.Now;
            if (now >= until) { _apiBlockedUntil = null; return null; }
            return Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        }
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var apiDetails = await ApiDetailsIfAvailableAsync(standardUrl, additionalSettings, cancellationToken);
        if (apiDetails != null) return apiDetails;
        return await FeedDetailsAsync(standardUrl, additionalSettings, cancellationToken);
    }

    private async Task<AssetDetails?> ApiDetailsIfAvailableAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken)
    {
        var explicitId = GetString(additionalSettings, "appId");
        var pname = explicitId != null && PackagePattern.IsMatch(explicitId) ? explicitId : null;
        if (pname == null) return null;
        var uri = Uri.TryCreate(standardUrl, UriKind.Absolute, out var u) ? u : null;
        if (uri == null || !ApiHostAllowed(uri)) return null;
        if (ApiBlockedMinutes is { } blocked)
            throw new ObtainiumHttpException(429, $"APKMirror API blocked for {blocked} more minutes");

        var info = await GetApiInfoAsync(pname, cancellationToken);
        if (info == null)
        {
            if (ApiBlockedMinutes is { } blockedAfter)
                throw new ObtainiumHttpException(429, $"APKMirror API blocked for {blockedAfter} more minutes");
            return null;
        }
        if (!info.Exists || string.IsNullOrEmpty(info.Version) || info.ReleaseLink == null)
            throw new NoReleasesException();
        if (!ApiEntryMatchesStandardUrl(info.AppLink, standardUrl)) return null;

        var fallbackToOlder = GetBool(additionalSettings, "fallbackToOlderReleases");
        var regexFilter = GetString(additionalSettings, "filterReleaseTitlesByRegEx");
        if (string.IsNullOrEmpty(regexFilter)) regexFilter = null;
        if (regexFilter != null)
        {
            var title = $"{info.AppName ?? ""} {info.Version} by {info.DeveloperName ?? ""}".Trim();
            if (!PassesRegExFilter(new Regex(regexFilter), title,
                    GetBool(additionalSettings, "invertReleaseTitlesFilter")))
            {
                if (fallbackToOlder) return null;
                throw new NoReleasesException();
            }
        }
        var releaseUrl = AbsoluteApiLink(info.ReleaseLink, standardUrl);
        if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var releaseUri) || !ApiHostAllowed(releaseUri))
            return null;
        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") ?? 0;
        var tooYoung = minAgeDays > 0 && info.ReleaseDate is { } rd
            && DateTimeOffset.UtcNow - rd < TimeSpan.FromDays(minAgeDays);
        if (tooYoung && fallbackToOlder) return null;
        // Windows mapping: no current-app context, so every release is "new".
        string? changeLog = info.WhatsNew;
        long? sizeBytes = null;
        if (!tooYoung)
        {
            var scrape = await ScrapeReleaseDetailsAsync(releaseUrl, cancellationToken);
            changeLog ??= scrape.ChangeLog;
            sizeBytes = scrape.SizeBytes;
        }
        return new AssetDetails(
            info.Version,
            new List<KeyValuePair<string, string>>(),
            GetAppNames(standardUrl),
            info.ReleaseDate,
            changeLog,
            new List<KeyValuePair<string, string>>(),
            null,
            null)
        { ReleaseUrl = releaseUrl, AppId = pname, ApkSizeBytes = sizeBytes };
    }

    private async Task<AssetDetails> FeedDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken)
    {
        var fallbackToOlder = GetBool(additionalSettings, "fallbackToOlderReleases");
        var regexFilter = GetString(additionalSettings, "filterReleaseTitlesByRegEx");
        if (string.IsNullOrEmpty(regexFilter)) regexFilter = null;

        using var res = await _http.SendAsync(
            BuildRequest(HttpMethod.Get, $"{standardUrl}/feed/"), cancellationToken);
        if ((int)res.StatusCode != 200)
            throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        var html = new HtmlDocument();
        html.LoadHtml(body);
        var items = html.DocumentNode.SelectNodes("//item") ?? new HtmlNodeCollection(null);

        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") ?? 0;
        DateTimeOffset? ReleaseDateFor(HtmlNode item)
        {
            // HtmlAgilityPack lowercases tag names.
            var pubDateRaw = item.SelectSingleNode("pubdate")?.InnerHtml;
            var dateString = pubDateRaw == null ? null : string.Join(' ', pubDateRaw.Split(' ').Take(5));
            if (dateString == null) return null;
            return DateTimeOffset.TryParseExact(dateString + " GMT", "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        }

        HtmlNode? targetRelease = null, tooYoungRelease = null;
        int? targetReleaseIndex = null, tooYoungReleaseIndex = null;
        var releaseSkipped = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!fallbackToOlder && i > releaseSkipped) break;
            var nameToFilter = items[i].SelectSingleNode("title")?.InnerHtml;
            if (regexFilter != null && nameToFilter != null
                && !PassesRegExFilter(new Regex(regexFilter), nameToFilter.Trim(),
                    GetBool(additionalSettings, "invertReleaseTitlesFilter")))
                continue;
            var date = ReleaseDateFor(items[i]);
            if (minAgeDays > 0 && date is { } d && DateTimeOffset.UtcNow - d < TimeSpan.FromDays(minAgeDays))
            {
                tooYoungRelease ??= items[i];
                tooYoungReleaseIndex ??= i;
                releaseSkipped++;
                continue;
            }
            targetRelease = items[i];
            targetReleaseIndex = i;
            break;
        }
        // No release old enough: use the newest so the caller can suppress it.
        targetRelease ??= tooYoungRelease;
        targetReleaseIndex ??= tooYoungReleaseIndex;
        if (targetRelease == null) throw new NoReleasesException();
        var titleString = targetRelease.SelectSingleNode("title")?.InnerHtml;
        var releaseDate = ReleaseDateFor(targetRelease);
        var releaseUrl = targetReleaseIndex != null
            ? ReleaseUrlFromFeedBodyForItemIndex(body, targetReleaseIndex.Value)
            : null;
        string? version = null;
        if (titleString != null)
            version = VersionFromTitle(titleString) ?? CleanReleaseTitle(titleString);
        if (string.IsNullOrEmpty(version)) version = titleString;
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();

        var tooYoung = minAgeDays > 0 && releaseDate is { } rd2
            && DateTimeOffset.UtcNow - rd2 < TimeSpan.FromDays(minAgeDays);
        // Windows mapping: no current-app context; a present release URL is always new.
        string? changeLog = null;
        long? sizeBytes = null;
        if (!string.IsNullOrEmpty(releaseUrl) && !tooYoung)
        {
            var scrape = await ScrapeReleaseDetailsAsync(releaseUrl, cancellationToken);
            changeLog = scrape.ChangeLog;
            sizeBytes = scrape.SizeBytes;
        }
        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>>(),
            GetAppNames(standardUrl),
            releaseDate,
            changeLog,
            new List<KeyValuePair<string, string>>(),
            null,
            null)
        { ReleaseUrl = releaseUrl, AppId = PackageFromFeedBody(body), ApkSizeBytes = sizeBytes };
    }

    private async Task<(string? ChangeLog, long? SizeBytes)> ScrapeReleaseDetailsAsync(
        string releaseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var releaseRes = await _http.SendAsync(
                BuildRequest(HttpMethod.Get, releaseUrl), cancellationToken);
            if ((int)releaseRes.StatusCode != 200) return (null, null);
            var body = await releaseRes.Content.ReadAsStringAsync(cancellationToken);
            var changeLog = ChangeLogFromReleasePageHtml(body);
            var doc = new HtmlDocument();
            doc.LoadHtml(body);
            var size = SizeBytesFromPageText(doc.DocumentNode.InnerText);
            if (size == null)
            {
                var downloadPageUrl = DownloadPageUrlFromReleasePage(body, releaseUrl);
                if (downloadPageUrl != null)
                {
                    using var downloadRes = await _http.SendAsync(
                        BuildRequest(HttpMethod.Get, downloadPageUrl), cancellationToken);
                    if ((int)downloadRes.StatusCode == 200)
                    {
                        var downloadDoc = new HtmlDocument();
                        downloadDoc.LoadHtml(await downloadRes.Content.ReadAsStringAsync(cancellationToken));
                        size = SizeBytesFromPageText(downloadDoc.DocumentNode.InnerText);
                    }
                }
            }
            return (changeLog, size);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<ApiAppInfo?> GetApiInfoAsync(string pname, CancellationToken cancellationToken)
    {
        if (_apiCache.TryGetValue(pname, out var cached))
        {
            if (DateTimeOffset.Now - cached.FetchedAt <= ApiCacheTtl) return cached.Info;
            _apiCache.Remove(pname);
        }
        try
        {
            using var res = await _http.SendAsync(BuildRequest(HttpMethod.Post, ApiUrl,
                new StringContent(JsonSerializer.Serialize(new { pnames = new[] { pname } }),
                    Encoding.UTF8, "application/json")), cancellationToken);
            if ((int)res.StatusCode != 200)
            {
                RecordApiFailure((int)res.StatusCode, res.Headers);
                return null;
            }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var raw in data.EnumerateArray())
            {
                var info = ParseApiAppInfo(raw);
                if (info == null) continue;
                _apiCache[info.Pname] = (info, DateTimeOffset.Now);
                if (info.Pname == pname) return info;
            }
            return null;
        }
        catch (ObtainiumSourceException) { throw; }
        catch { return null; }
    }

    private void RecordApiFailure(int statusCode, System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var now = DateTimeOffset.Now;
        IEnumerable<string>? values;
        if (headers.TryGetValues("cf-mitigated", out values)
            && values.FirstOrDefault()?.Trim().Equals("challenge", StringComparison.OrdinalIgnoreCase) == true)
        {
            _apiBlockedUntil = now + ApiTransientBlockDuration;
            return;
        }
        if (statusCode is 401 or 403) { _apiBlockedUntil = now + ApiAuthBlockDuration; return; }
        if (statusCode is 429 or 503)
        {
            var retryAfter = headers.TryGetValues("retry-after", out values)
                && int.TryParse(values.FirstOrDefault()?.Trim(), out var seconds) && seconds > 0
                ? seconds : 300;
            _apiBlockedUntil = now + TimeSpan.FromSeconds(retryAfter);
            return;
        }
        _apiBlockedUntil = now + ApiTransientBlockDuration;
    }

    private static ApiAppInfo? ParseApiAppInfo(JsonElement json)
    {
        var pname = json.TryGetProperty("pname", out var pn) ? pn.GetString() ?? "" : "";
        if (pname.Length == 0) return null;
        var exists = json.TryGetProperty("exists", out var ex) && ex.ValueKind == JsonValueKind.True;
        string? appName = null, appLink = null, developerName = null, version = null, whatsNew = null, releaseLink = null;
        DateTimeOffset? releaseDate = null;
        if (json.TryGetProperty("app", out var app) && app.ValueKind == JsonValueKind.Object)
        {
            appName = app.TryGetProperty("name", out var n) ? n.GetString() : null;
            appLink = app.TryGetProperty("link", out var l) ? l.GetString() : null;
        }
        if (json.TryGetProperty("developer", out var dev) && dev.ValueKind == JsonValueKind.Object)
            developerName = dev.TryGetProperty("name", out var dn) ? dn.GetString() : null;
        if (json.TryGetProperty("release", out var rel) && rel.ValueKind == JsonValueKind.Object)
        {
            version = rel.TryGetProperty("version", out var v) ? v.GetString() : null;
            releaseDate = ApiDate(rel.TryGetProperty("publish_date", out var pd) ? pd.GetString() : null);
            whatsNew = PlainTextFromHtml(rel.TryGetProperty("whats_new", out var wn) ? wn.GetString() : null);
            releaseLink = rel.TryGetProperty("link", out var rl) ? rl.GetString() : null;
        }
        return new ApiAppInfo(pname, exists, appName, appLink, developerName, version, releaseDate, whatsNew, releaseLink);
    }

    internal static AppNames GetAppNames(string standardUrl)
    {
        var temp = standardUrl.Substring(standardUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        var pathStart = temp.IndexOf('/');
        if (pathStart < 0 || pathStart + 1 >= temp.Length) throw new InvalidUrlException("APKMirror");
        var names = temp.Substring(pathStart + 1).Split('/');
        if (names.Length < 3) throw new InvalidUrlException("APKMirror");
        return new AppNames(names[1], names[2]);
    }

    /// Parses the API's `YYYY-MM-DD HH:MM:SS` (UTC) publish dates.
    internal static DateTimeOffset? ApiDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().Replace(' ', 'T');
        if (!normalized.EndsWith('Z')) normalized += "Z";
        return DateTimeOffset.TryParse(normalized, null, DateTimeStyles.RoundtripKind, out var d) ? d.ToUniversalTime() : null;
    }

    /// Converts the API's whats_new HTML into the plain text the UI renders.
    internal static string? PlainTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var withBreaks = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        withBreaks = Regex.Replace(withBreaks, @"</(?:p|div|li|ul|ol|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        var doc = new HtmlDocument();
        doc.LoadHtml(withBreaks);
        var text = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? withBreaks;
        var normalized = string.Join('\n', text.Split('\n')
            .Select(line => Regex.Replace(HtmlEntity.DeEntitize(line), @"[ \t]+", " ").Trim())
            .Where(line => line.Length > 0)).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    /// Whether an app_exists entry describes the listing the user tracks.
    internal static bool ApiEntryMatchesStandardUrl(string? appLink, string standardUrl)
    {
        if (string.IsNullOrWhiteSpace(appLink)) return false;
        var trackedPath = Uri.TryCreate(standardUrl, UriKind.Absolute, out var u) ? u.AbsolutePath : null;
        if (string.IsNullOrEmpty(trackedPath)) return false;
        static string Normalize(string path)
        {
            var p = path.Trim();
            if (!p.StartsWith('/')) p = "/" + p;
            while (p.Length > 1 && p.EndsWith('/')) p = p.Substring(0, p.Length - 1);
            return p.ToLowerInvariant();
        }
        // The API link may be relative; compare as a path.
        var linkPath = Uri.TryCreate(appLink, UriKind.Absolute, out var lu) ? lu.AbsolutePath : appLink;
        return Normalize(linkPath) == Normalize(trackedPath);
    }

    /// Reads the package name from the first icon URL in a feed body's content.
    internal static string? PackageFromFeedBody(string feedBody)
    {
        var match = Regex.Match(feedBody, "<img[^>]+src=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? PackageFromIconUrl(match.Groups[1].Value) : null;
    }

    /// Extracts the release page URL of the Nth item from the raw feed body.
    internal static string? ReleaseUrlFromFeedBodyForItemIndex(string body, int index)
    {
        if (index < 0) return null;
        var segments = Regex.Split(body, "<item\\b[^>]*>", RegexOptions.IgnoreCase);
        if (index + 1 >= segments.Length) return null;
        var afterOpen = segments[index + 1];
        var closeIndex = afterOpen.IndexOf("</item>", StringComparison.OrdinalIgnoreCase);
        var itemXml = closeIndex >= 0 ? afterOpen.Substring(0, closeIndex) : afterOpen;
        var match = Regex.Match(itemXml, "<link>\\s*([^<]+?)\\s*</link>", RegexOptions.IgnoreCase);
        var url = match.Success ? match.Groups[1].Value : null;
        return url != null && (url.StartsWith("http://") || url.StartsWith("https://")) ? url : null;
    }

    /// Strips title decorations (" by Author", "(arm64-v8a)", "[0]").
    internal static string CleanReleaseTitle(string title)
    {
        var cleaned = title;
        var byIndex = cleaned.LastIndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIndex > 0) cleaned = cleaned.Substring(0, byIndex);
        cleaned = Regex.Replace(cleaned, @"\([^)]*\)", " ");
        cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    /// Extracts a bare version token (e.g. "42.5.15-21") from a title.
    internal static string? VersionFromTitle(string title)
    {
        var match = Regex.Match(CleanReleaseTitle(title), @"(?:^|\s)v?(\d[\d.\-+_]*)(?=\s|$)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Extracts a package name from a listing's og:image filename.
    internal static string? PackageFromIconUrl(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return null;
        if (!Uri.TryCreate(iconUrl.Trim(), UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        var fileName = segments[segments.Length - 1];
        var dotIndex = fileName.LastIndexOf('.');
        var stem = dotIndex > 0 ? fileName.Substring(0, dotIndex) : fileName;
        var unsizedStem = Regex.Replace(stem, @"-\d+x\d+$", "");
        var unsizedSegments = unsizedStem.Split('_');
        for (var i = unsizedSegments.Length - 1; i >= 0; i--)
        {
            var segment = unsizedSegments[i];
            if (!PackagePattern.IsMatch(segment)) continue;
            if (segment.Contains("apkmirror", StringComparison.OrdinalIgnoreCase)) continue;
            return segment;
        }
        return null;
    }

    /// Parses the "File size: 270.70 MB" line from page text.
    internal static long? SizeBytesFromPageText(string text)
    {
        var match = Regex.Match(text, @"File size:\s*([0-9][0-9.]*)\s*(B|KB|MB|GB)\b", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var size))
            return null;
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "GB" => 1L << 30,
            "MB" => 1L << 20,
            "KB" => 1L << 10,
            _ => 1L,
        };
        return (long)Math.Round(size * multiplier);
    }

    /// Finds the first same-release "-apk-download/" page link on a release page.
    internal static string? DownloadPageUrlFromReleasePage(string html, string releasePageUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        if (!Uri.TryCreate(releasePageUrl, UriKind.Absolute, out var releaseUri)) return null;
        var releaseBase = releaseUri.GetLeftPart(UriPartial.Path);
        var validPrefix = releaseBase.EndsWith('/') ? releaseBase : releaseBase + "/";
        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links == null) return null;
        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", null);
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (!Uri.TryCreate(releaseUri, href, out var absolute)) continue;
            var builder = new UriBuilder(absolute) { Fragment = "" };
            var path = builder.Uri.AbsolutePath.EndsWith('/')
                ? builder.Uri.AbsolutePath : builder.Uri.AbsolutePath + "/";
            if (!path.EndsWith("-apk-download/")) continue;
            builder.Path = path;
            var url = builder.Uri.ToString();
            if (!url.StartsWith(validPrefix, StringComparison.Ordinal)) continue;
            return url;
        }
        return null;
    }

    private static bool IsWhatsNewHeading(string text) =>
        text.StartsWith("what's new in ", StringComparison.OrdinalIgnoreCase);

    private static bool IsChangeLogStop(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.StartsWith("about ") || lower.StartsWith("download ")
            || lower.Contains(" screenshots") || lower.Contains(" trailer");
    }

    private static bool IsChangeLogNoise(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower == "advertisement" || lower.StartsWith("verified safe to install")
            || lower == "scroll to available downloads"
            || lower == "a more recent upload may be available below!";
    }

    private static string NormalizedText(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string BlockText(HtmlNode element)
    {
        var parts = new List<string>();
        foreach (var child in element.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            if (child.Name == "ul" || child.Name == "ol")
            {
                foreach (var item in child.SelectNodes(".//li") ?? new HtmlNodeCollection(null))
                {
                    var text = NormalizedText(HtmlEntity.DeEntitize(item.InnerText));
                    if (text.Length > 0) parts.Add("- " + text);
                }
            }
            else
            {
                var text = NormalizedText(HtmlEntity.DeEntitize(child.InnerText));
                if (text.Length > 0) parts.Add(text);
            }
        }
        if (parts.Count > 0) return string.Join('\n', parts);
        return NormalizedText(HtmlEntity.DeEntitize(element.InnerText));
    }

    /// Extracts the "What's new in ..." block from a release page.
    internal static string? ChangeLogFromReleasePageHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        HtmlNode? heading = null;
        var headings = doc.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//h5|//h6");
        if (headings != null)
        {
            foreach (var candidate in headings)
            {
                if (IsWhatsNewHeading(NormalizedText(HtmlEntity.DeEntitize(candidate.InnerText))))
                {
                    heading = candidate;
                    break;
                }
            }
        }
        if (heading == null) return null;
        var parts = new List<string>();
        var sibling = heading.NextSibling;
        while (sibling != null)
        {
            if (sibling.NodeType == HtmlNodeType.Element)
            {
                var text = BlockText(sibling);
                if (IsChangeLogStop(text)) break;
                if (text.Length > 0 && !IsChangeLogNoise(text)) parts.Add(text);
            }
            sibling = sibling.NextSibling;
        }
        var changeLog = string.Join("\n\n", parts).Trim();
        return changeLog.Length == 0 ? null : changeLog;
    }

    private static string AbsoluteApiLink(string link, string standardUrl)
    {
        if (link.StartsWith("http://") || link.StartsWith("https://")) return link;
        return new Uri(new Uri(standardUrl), link).ToString();
    }
}
