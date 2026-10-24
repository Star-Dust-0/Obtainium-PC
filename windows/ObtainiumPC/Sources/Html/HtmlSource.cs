using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ObtainiumPC.Services.Versioning;

namespace ObtainiumPC.Sources.Html;

/// <summary>
/// Port of lib/app_sources/html.dart: the generic fallback source. Scrapes a
/// page (optionally following intermediate link steps), picks the last matching
/// link, extracts a version via regex or a pseudo-versioning method, and
/// optionally scrapes a release date.
///
/// Documented platform mappings:
/// - "APK" wording becomes assets; container detection uses Windows extensions.
/// - APKLinkHash uses a stable SHA-256 based hash (Dart's string hashCode is
///   not stable across runtimes, so exact hash parity is impossible; the value
///   only needs to change when the link changes).
/// - partialAPKHash hashes the fetched bytes as a single chunk (upstream hashes
///   network-arrival chunks, which is itself timing-dependent; self-consistency
///   is what matters).
/// - intl date patterns are mapped to .NET formats on a best-effort basis.
/// </summary>
public sealed class HtmlSource : AppSourceBase
{
    private const int PartialHashStartingSize = 1024;
    private const int PartialHashLowerLimit = 128;
    private const int PartialHashDecrement = 256;
    private const int MaxIntermediateLinkDepth = 10;

    private readonly HttpClient _http;

    public HtmlSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
    }

    public override string Name => "HTML";
    public override IReadOnlyList<string> Hosts => Array.Empty<string>();
    public override bool SuppressStandardVersionExtraction => true;
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) => url;

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } = BuildFormItems();

    private static IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> BuildFormItems()
    {
        Forms.OptionFormItem H(string label) =>
            new() { Key = "", Label = label, Kind = Forms.OptionFormItemKind.Header };
        Forms.OptionFormItem Sw(string key, string label) =>
            new() { Key = key, Label = label, Kind = Forms.OptionFormItemKind.Switch };
        Forms.OptionFormItem Tx(string key, string label, string? hint = null, bool rx = true) =>
            new() { Key = key, Label = label, Kind = Forms.OptionFormItemKind.Text, Hint = hint, ValidateRegex = rx };

        var common = new Forms.OptionFormItem[]
        {
            Sw("filterByLinkText", "Filter by link text"),
            Sw("matchLinksOutsideATags", "Match links outside <a> tags"),
            Sw("skipSort", "Skip link sorting"),
            Sw("reverseSort", "Take first link (reverse sort)"),
            Sw("sortByLastLinkSegment", "Sort by last link segment"),
        };

        var rows = new List<IReadOnlyList<Forms.OptionFormItem>>
        {
            new[] { H("Link options") },
            // The intermediate-link sub-form is edited as a single regex + arch
            // toggle (one step). The engine supports the full list format.
            new[] { new Forms.OptionFormItem { Key = "intermediateLink", Label = "Intermediate link regex (one step; follows a matching link before scraping)", Kind = Forms.OptionFormItemKind.Text, Hint = @"([0-9]+.)*[0-9]+/$", ValidateRegex = true, SpecialCodec = "intermediateLink" } },
            new[] { new Forms.OptionFormItem { Key = "intermediateLinkAutoArch", Label = "Filter intermediate links by CPU architecture", Kind = Forms.OptionFormItemKind.Switch, SpecialCodec = "intermediateLinkAutoArch" } },
            new[] { Tx("customLinkFilterRegex", "Filter links by custom RegEx", @"download/(.*/)?(windows|exe|msi)") },
        };
        foreach (var c in common) rows.Add(new[] { c });
        rows.Add(new[] { H("Version options") });
        rows.Add(new[] { Sw("versionExtractWholePage", "Extract version string from entire page") });
        rows.Add(new[] { new Forms.OptionFormItem
        {
            Key = "defaultPseudoVersioningMethod", Label = "Pseudo-versioning method", Kind = Forms.OptionFormItemKind.ChoiceList, DefaultString = "partialAPKHash",
            Options = new KeyValuePair<string, string>[]
            {
                new("partialAPKHash", "Partial file hash"),
                new("APKLinkHash", "Link hash"),
                new("ETag", "ETag"),
            }
        } });
        rows.Add(new[] { H("Release date") });
        rows.Add(new[] { Tx("releaseDateRegEx", "Release date RegEx", @"Last modified: (.*)") });
        rows.Add(new[] { Tx("releaseDateMatchGroup", "Match group to use for \"Release date RegEx\"", "$0", rx: false) });
        rows.Add(new[] { Tx("releaseDateDateFormat", "Release date format", "EEE. MMMM d, yyyy - h:mma", rx: false) });
        rows.Add(new[] { H("Request headers") });
        rows.Add(new[] { new Forms.OptionFormItem { Key = "requestHeader", Label = "Request headers (one per line, 'Name: value')", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = false, SpecialCodec = "requestHeader" } });
        rows.Add(new[] { H("ZIP options") });
        rows.Add(new[] { Tx("zippedApkFilterRegEx", "Filter assets inside ZIP") });
        return rows;
    }

    // ------------------------------------------------------------------
    // Request headers (per-app, stored as [{"requestHeader": "Name: value"}])
    // ------------------------------------------------------------------

    public static Dictionary<string, string> BuildRequestHeaders(IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var result = new Dictionary<string, string>();
        if (!additionalSettings.TryGetValue("requestHeader", out var rh) || rh.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var entry in rh.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("requestHeader", out var hv) || hv.ValueKind != JsonValueKind.String) continue;
            var line = hv.GetString();
            if (string.IsNullOrEmpty(line)) continue;
            var parts = line.Split(':');
            if (parts.Length < 2) continue;
            result[parts[0].Trim()] = string.Join(':', parts.Skip(1)).Trim();
        }
        return result;
    }

    private HttpRequestMessage BuildRequest(string url, IReadOnlyDictionary<string, JsonElement> settings)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var kv in BuildRequestHeaders(settings))
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        return req;
    }

    // ------------------------------------------------------------------
    // Link grabbing
    // ------------------------------------------------------------------

    public static string EnsureAbsoluteUrl(string ambiguousUrl, Uri referenceAbsoluteUrl)
    {
        ambiguousUrl = ambiguousUrl.Trim();
        if (Uri.TryCreate(ambiguousUrl, UriKind.Absolute, out _)) return ambiguousUrl;
        return new Uri(referenceAbsoluteUrl, ambiguousUrl).ToString();
    }

    private static List<string> CollectAllStrings(JsonElement obj)
    {
        var results = new List<string>();
        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    if (el.GetString() is { } s) results.Add(s);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray()) Walk(item);
                    break;
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject()) Walk(prop.Value);
                    break;
            }
        }
        Walk(obj);
        return results;
    }

    private static List<KeyValuePair<string, string>> GetLinksInLines(string lines) =>
        Regex.Matches(lines, """(?:(?:http|https|ftp)://)[^\s"'<>()\[\]{}]+""")
            .Select(m => Regex.Replace(m.Value, @"[.,;:!?]+$", ""))
            .Where(url => url.Length > 0)
            .Select(url => new KeyValuePair<string, string>(url, url.Split('/').Last()))
            .ToList();

    private static List<KeyValuePair<string, string>> GetLinksInHtmlAttributes(HtmlDocument html, Uri reqUrl)
    {
        var links = new List<KeyValuePair<string, string>>();
        foreach (var element in html.DocumentNode.SelectNodes("//*") ?? Enumerable.Empty<HtmlNode>())
        {
            foreach (var attr in element.Attributes)
            {
                var trimmed = attr.Value.Trim();
                if (trimmed.Length == 0) continue;
                var isAbsolute = Regex.IsMatch(trimmed, @"^(https?|ftp)://", RegexOptions.IgnoreCase);
                if (!isAbsolute && !trimmed.StartsWith('/')) continue;
                string resolved;
                try { resolved = EnsureAbsoluteUrl(trimmed, reqUrl); } catch (UriFormatException) { continue; }
                if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri)) continue;
                if (uri.Scheme is not ("http" or "https" or "ftp")) continue;
                links.Add(new KeyValuePair<string, string>(resolved, resolved.Split('/').Last()));
            }
        }
        return links;
    }

    /// Keys are URLs, values are filenames (opposite to AssetDetails.AssetUrls), as upstream.
    public List<KeyValuePair<string, string>> GrabLinksCommon(
        string rawBody, Uri reqUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var matchLinksOutsideATags = GetBool(additionalSettings, "matchLinksOutsideATags");
        var html = new HtmlDocument();
        html.LoadHtml(rawBody);

        var allLinks = (html.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
            .Select(a => new KeyValuePair<string, string>(
                a.GetAttributeValue("href", ""),
                !string.IsNullOrWhiteSpace(a.InnerText) ? a.InnerText.Trim() : a.GetAttributeValue("href", "").Split('/').Last()))
            .Where(e => e.Key.Length > 0)
            .Select(e => new KeyValuePair<string, string>(EnsureAbsoluteUrl(e.Key, reqUrl), e.Value))
            .ToList();

        if (allLinks.Count == 0 || matchLinksOutsideATags)
        {
            var merged = allLinks.ToDictionary(e => e.Key, e => e);
            void AddAll(IEnumerable<KeyValuePair<string, string>> links)
            {
                foreach (var link in links) merged.TryAdd(link.Key, link);
            }

            if (allLinks.Count == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    var jsonStrings = CollectAllStrings(doc.RootElement);
                    var jsonLinks = GetLinksInLines(string.Join('\n', jsonStrings));
                    if (jsonLinks.Count == 0)
                        jsonLinks = GetLinksInLines(string.Join('\n', jsonStrings.Select(l =>
                        {
                            try { return EnsureAbsoluteUrl(l, reqUrl); } catch { return l; }
                        })));
                    AddAll(jsonLinks);
                }
                catch (JsonException)
                {
                    AddAll(GetLinksInLines(rawBody));
                }
            }
            if (matchLinksOutsideATags)
            {
                AddAll(GetLinksInLines(rawBody));
                AddAll(GetLinksInHtmlAttributes(html, reqUrl));
            }
            allLinks = merged.Values.ToList();
        }

        List<KeyValuePair<string, string>> links;
        var skipSort = GetBool(additionalSettings, "skipSort");
        var filterLinkByText = GetBool(additionalSettings, "filterByLinkText");
        var customFilter = GetString(additionalSettings, "customLinkFilterRegex");
        if (!string.IsNullOrEmpty(customFilter))
        {
            var reg = new Regex(customFilter);
            links = allLinks.Where(e =>
            {
                var link = e.Key;
                try { link = Uri.UnescapeDataString(e.Key); } catch { }
                return reg.IsMatch(filterLinkByText ? e.Value : link);
            }).ToList();
        }
        else
        {
            links = allLinks.Where(e =>
            {
                var link = e.Key;
                try { link = Uri.UnescapeDataString(e.Key); } catch { }
                var candidate = (filterLinkByText ? e.Value : link).Trim();
                string path;
                try { path = new Uri(candidate).AbsolutePath; } catch { return false; }
                return AssetFilterService.IsAppOrContainerFile(path);
            }).ToList();
        }

        if (!skipSort)
        {
            var byLastSegment = GetBool(additionalSettings, "sortByLastLinkSegment");
            links.Sort((a, b) => byLastSegment
                ? StringCompare.CompareAlphaNumeric(
                    a.Key.Split('/').LastOrDefault(s => s.Length > 0) ?? "",
                    b.Key.Split('/').LastOrDefault(s => s.Length > 0) ?? "")
                : StringCompare.CompareAlphaNumeric(a.Key, b.Key));
        }
        if (GetBool(additionalSettings, "reverseSort")) links.Reverse();
        return links;
    }

    // ------------------------------------------------------------------
    // Release date extraction
    // ------------------------------------------------------------------

    private static readonly (string Intl, string Net)[] DateFormatTokens =
    {
        ("EEEE", "dddd"), ("EEE", "ddd"), ("MMMM", "MMMM"), ("MMM", "MMM"),
        ("yyyy", "yyyy"), ("dd", "dd"), ("hh", "hh"), ("HH", "HH"),
        ("mm", "mm"), ("ss", "ss"), ("a", "tt"), ("d", "d"), ("h", "h"), ("H", "H"), ("M", "M"),
    };

    private static DateTimeOffset? ParseHtmlReleaseDateText(
        string? releaseDateRegEx, string? matchGroup, string? dateFormatString, string stringToCheck)
    {
        if (string.IsNullOrEmpty(releaseDateRegEx)) return null;
        string? extracted;
        try { extracted = VersionService.ExtractVersion(releaseDateRegEx, matchGroup, stringToCheck); }
        catch (NoVersionException) { return null; }
        if (string.IsNullOrEmpty(extracted)) return null;

        // Scraped pages often use lowercase pm and ordinal suffixes (3rd).
        var normalized = Regex.Replace(extracted, @"(\d+)(st|nd|rd|th)\b", "$1", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"(?<=\d)\s*([ap]m)\b", m => m.Groups[1].Value.ToUpperInvariant(), RegexOptions.IgnoreCase);

        var dateFormat = dateFormatString?.Trim();
        if (!string.IsNullOrEmpty(dateFormat))
        {
            try
            {
                var netFormat = dateFormat;
                foreach (var (intl, net) in DateFormatTokens)
                    netFormat = Regex.Replace(netFormat, Regex.Escape(intl) + "(?![a-zA-Z])", net);
                if (DateTimeOffset.TryParseExact(normalized, netFormat,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var exact))
                    return exact;
            }
            catch { /* fall through to loose parsing */ }
        }
        return DateTimeOffset.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var loose) ? loose : null;
    }

    private static bool SameHtmlLink(string a, string b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var ua) || !Uri.TryCreate(b, UriKind.Absolute, out var ub))
            return a == b;
        return ua.GetLeftPart(UriPartial.Path).TrimEnd('/') == ub.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static DateTimeOffset? ExtractHtmlReleaseDateFromLink(
        string? releaseDateRegEx, string? matchGroup, string? dateFormatString,
        string pageHtml, string selectedLink, Uri baseUri)
    {
        HtmlDocument document;
        try
        {
            document = new HtmlDocument();
            document.LoadHtml(pageHtml);
        }
        catch { return null; }
        foreach (var anchor in document.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = anchor.GetAttributeValue("href", "").Trim();
            if (href.Length == 0) continue;
            string resolved;
            try { resolved = EnsureAbsoluteUrl(href, baseUri); } catch { continue; }
            if (!SameHtmlLink(resolved, selectedLink)) continue;
            HtmlNode? element = anchor;
            for (var depth = 0; depth < 4 && element != null; depth++)
            {
                var text = element.InnerText.Trim().Replace("\r\n", "\n").Replace("\n", "\\n") + "\\n";
                if (text.Length > 0)
                {
                    var date = ParseHtmlReleaseDateText(releaseDateRegEx, matchGroup, dateFormatString, text);
                    if (date != null) return date;
                }
                element = element.ParentNode;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Pseudo-versioning
    // ------------------------------------------------------------------

    private static string Sha256Hex(string input, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant().Substring(0, length);

    private async Task<string?> CheckETagHeader(string url, IReadOnlyDictionary<string, JsonElement> settings, CancellationToken ct)
    {
        using var res = await _http.SendAsync(BuildRequest(url, settings), HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)res.StatusCode < 200 || (int)res.StatusCode >= 300) return null;
        var etag = res.Headers.ETag?.Tag?.Replace("\"", "");
        return etag != null ? Sha256Hex(etag, 12) : null;
    }

    private async Task<string> CheckPartialDownloadHash(string url, int bytesToGrab, IReadOnlyDictionary<string, JsonElement> settings, CancellationToken ct)
    {
        var req = BuildRequest(url, settings);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, bytesToGrab);
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)res.StatusCode < 200 || (int)res.StatusCode > 299)
            throw new ObtainiumHttpException((int)res.StatusCode, url);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        // Single-chunk equivalent of upstream's JSON(list-of-chunks) hash.
        var json = JsonSerializer.Serialize(new[] { bytes });
        return Sha256Hex(json, 8);
    }

    private async Task<string> CheckPartialDownloadHashDynamic(string url, IReadOnlyDictionary<string, JsonElement> settings, CancellationToken ct)
    {
        for (var i = PartialHashStartingSize; i >= PartialHashLowerLimit; i -= PartialHashDecrement)
        {
            var a = await CheckPartialDownloadHash(url, i, settings, ct);
            var b = await CheckPartialDownloadHash(url, i, settings, ct);
            if (a == b) return a;
        }
        throw new NoVersionException();
    }

    // ------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var currentUrl = standardUrl;

        // Follow intermediate link steps (stored as a list of {customLinkFilterRegex, ...}).
        var intermediateLinks = new List<(string RegexText, bool AutoArch, Dictionary<string, JsonElement> Settings)>();
        if (additionalSettings.TryGetValue("intermediateLink", out var il) && il.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in il.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var dict = entry.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var rx = GetString(dict, "customLinkFilterRegex");
                if (string.IsNullOrEmpty(rx)) continue;
                intermediateLinks.Add((rx, GetBool(dict, "autoLinkFilterByArch"), dict));
            }
        }
        foreach (var step in intermediateLinks.Take(MaxIntermediateLinkDepth))
        {
            using var stepRes = await _http.SendAsync(BuildRequest(currentUrl, additionalSettings), cancellationToken);
            if ((int)stepRes.StatusCode >= 400) throw new ObtainiumHttpException((int)stepRes.StatusCode, currentUrl);
            var stepBody = await stepRes.Content.ReadAsStringAsync(cancellationToken);
            var stepSettings = new Dictionary<string, JsonElement>(additionalSettings);
            foreach (var kv in step.Settings) stepSettings[kv.Key] = kv.Value;
            var intLinks = GrabLinksCommon(stepBody, new Uri(currentUrl), stepSettings);
            if (step.AutoArch)
                intLinks = AssetFilterService.FilterAssetsByArch(intLinks, Services.Updates.AppUpdateService.CurrentArches());
            if (intLinks.Count == 0) throw new NoReleasesException();
            currentUrl = intLinks.Last().Key;
        }

        var uri = new Uri(currentUrl);
        List<KeyValuePair<string, string>> links;
        var versionExtractionWholePageString = currentUrl;
        string? pageHtml = null;
        if (!GetBool(additionalSettings, "directAPKLink"))
        {
            using var res = await _http.SendAsync(BuildRequest(currentUrl, additionalSettings), cancellationToken);
            if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, currentUrl);
            pageHtml = await res.Content.ReadAsStringAsync(cancellationToken);
            versionExtractionWholePageString = pageHtml.Replace("\r\n", "\n").Replace("\n", "\\n");
            links = GrabLinksCommon(pageHtml, uri, additionalSettings);
            var filterRegex = GetString(additionalSettings, "apkFilterRegEx");
            links = AssetFilterService.FilterAssets(links, filterRegex,
                GetBool(additionalSettings, "invertAPKFilter") ? true : (bool?)null);
            if (links.Count == 0) throw new NoReleasesException();
        }
        else
        {
            links = new List<KeyValuePair<string, string>> { new(currentUrl, currentUrl) };
        }

        var rel = links.Last().Key;
        string relDecoded;
        try { relDecoded = Uri.UnescapeDataString(rel); } catch { relDecoded = rel; }

        string? version = null;
        try
        {
            version = VersionService.ExtractVersion(
                GetString(additionalSettings, "versionExtractionRegEx"),
                GetString(additionalSettings, "matchGroupToUse"),
                GetBool(additionalSettings, "versionExtractWholePage") ? versionExtractionWholePageString : relDecoded);
        }
        catch (NoVersionException) { version = null; }

        var pseudoMethod = GetString(additionalSettings, "defaultPseudoVersioningMethod") ?? "partialAPKHash";
        if (version == null && pseudoMethod == "ETag")
        {
            version = await CheckETagHeader(rel, additionalSettings, cancellationToken);
            if (string.IsNullOrEmpty(version)) throw new NoVersionException();
        }
        version ??= pseudoMethod == "APKLinkHash"
            ? Sha256Hex(rel, 12)
            : await CheckPartialDownloadHashDynamic(rel, additionalSettings, cancellationToken);

        DateTimeOffset? releaseDate = null;
        var releaseDateRegEx = GetString(additionalSettings, "releaseDateRegEx");
        if (!string.IsNullOrEmpty(releaseDateRegEx))
        {
            if (pageHtml != null)
                releaseDate = ExtractHtmlReleaseDateFromLink(
                    releaseDateRegEx, GetString(additionalSettings, "releaseDateMatchGroup"),
                    GetString(additionalSettings, "releaseDateDateFormat"), pageHtml, rel, uri);
            releaseDate ??= ParseHtmlReleaseDateText(
                releaseDateRegEx, GetString(additionalSettings, "releaseDateMatchGroup"),
                GetString(additionalSettings, "releaseDateDateFormat"), versionExtractionWholePageString);
        }

        var relUri = new Uri(rel);
        var fileName = relUri.Segments.Length > 0 ? relUri.Segments[^1] : relUri.Host;
        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new(Sha256Hex(rel, 12) + "-" + fileName, rel) },
            new AppNames(uri.Host, "App"),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
