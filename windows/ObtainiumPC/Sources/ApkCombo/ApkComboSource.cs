using System.Net.Http;
using System.Text.Json;
using HtmlAgilityPack;

namespace ObtainiumPC.Sources.ApkCombo;

/// <summary>
/// Port of lib/app_sources/apkcombo.dart. Scraping needs a curl-style UA to
/// get past Cloudflare; downloads are R2 presigned URLs that expire, so fresh
/// links are re-scraped at download time and matched by the stable R2 object
/// path. Track-only on Windows.
/// </summary>
public sealed class ApkComboSource : AppSourceBase
{
    private const string CurlUserAgent = "curl/8.0.1";

    private readonly HttpClient _http;

    public ApkComboSource(HttpClient? http = null)
    {
        // Follow redirects manually is not needed; plain client with no
        // default UA so the curl-style one is the only one sent.
        _http = http ?? new HttpClient();
    }

    public override string Name => "APKCombo";
    public override IReadOnlyList<string> Hosts => new[] { "apkcombo.com" };
    public override bool SupportsInstall => false;
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/+[^/]+/+[^/]+");

    private static HttpRequestMessage BuildRequest(string url)
    {
        // A curl-style User-Agent is needed to get past Cloudflare. No
        // explicit Host header: the download is a presigned R2 URL on another
        // host and a wrong Host makes R2 reject the signed request.
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", CurlUserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        return req;
    }

    // Downloads also need the curl-style UA (same Cloudflare gate).
    public override IReadOnlyDictionary<string, string>? GetDownloadRequestHeaders(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings) =>
        new Dictionary<string, string>
        {
            ["User-Agent"] = CurlUserAgent,
            ["Accept"] = "*/*",
            ["Connection"] = "keep-alive",
        };

    private async Task<List<KeyValuePair<string, string>>> GetApkUrls(string standardUrl, CancellationToken ct)
    {
        var res = await _http.SendAsync(BuildRequest($"{standardUrl}/download/apk"), ct);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(ct));

        var urls = new List<KeyValuePair<string, string>>();
        var variants = html.DocumentNode.SelectNodes("//*[@id='variants-tab']/div/ul/li");
        foreach (var li in variants ?? Enumerable.Empty<HtmlNode>())
        {
            var arch = li.SelectSingleNode(".//code")?.InnerText.Trim()
                .Replace(",", "").Replace(":", "-").Replace(" ", "-");
            // Each variant lists its current build first, then older builds -
            // take only the first valid (current) download link per variant.
            foreach (var a in li.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
            {
                var url = a.GetAttributeValue("href", null);
                if (url != null)
                {
                    // Unwrap "/r2?u=<encoded presigned URL>" redirectors (issue #341).
                    // The href is site-relative, so match the path portion directly.
                    var qIdx = url.IndexOf('?');
                    var pathPart = qIdx >= 0 ? url[..qIdx] : url;
                    if (pathPart == "/r2" && qIdx >= 0)
                    {
                        var uParam = url[(qIdx + 1)..]
                            .Split('&', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Split('=', 2))
                            .Where(p => p.Length == 2 && p[0] == "u")
                            .Select(p => Uri.UnescapeDataString(p[1]))
                            .FirstOrDefault();
                        if (uParam != null) url = uParam;
                    }
                }
                if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var apkUri)
                    || !AssetFilterService.IsApkOrContainerFile(apkUri.AbsolutePath))
                    continue;
                var verCode = a.SelectSingleNode(".//*[contains(@class,'info')]//*[contains(@class,'header')]//*[contains(@class,'vercode')]")
                    ?.InnerText.Trim() ?? "";
                var segments = apkUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var fallbackName = segments.Length > 0 ? segments.Last() : $"app-{verCode}.apk";
                urls.Add(new KeyValuePair<string, string>(
                    arch != null ? $"{arch}-{verCode}.apk" : fallbackName,
                    url));
                break;
            }
        }
        return urls;
    }

    // The presigned URLs expire, so re-scrape fresh links at download time and
    // match the stored asset by its stable R2 object path.
    public override async Task<string> AssetUrlPrefetchModifierAsync(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var freshUrls = await GetApkUrls(standardUrl, cancellationToken);
        var pathToMatch = new Uri(assetUrl).AbsolutePath;
        foreach (var url in freshUrls)
            if (new Uri(url.Value).AbsolutePath == pathToMatch)
                return url.Value;
        throw new NoAppAssetException();
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        var res = await _http.SendAsync(BuildRequest(standardUrl), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));

        var version = html.DocumentNode.SelectSingleNode("//div[contains(@class,'version')]")?.InnerText.Trim();
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
        var appName = html.DocumentNode.SelectSingleNode("//div[contains(@class,'app_name')]")?.InnerText.Trim() ?? appId;
        var author = html.DocumentNode.SelectSingleNode("//div[contains(@class,'author')]")?.InnerText.Trim() ?? appName;

        var infoValues = (html.DocumentNode.SelectNodes("//div[contains(@class,'information-table')]//*[contains(@class,'item')]//div[contains(@class,'value')]")
                ?? Enumerable.Empty<HtmlNode>())
            .Select(e => e.InnerText.Trim()).ToList();
        DateTimeOffset? releaseDate = null;
        if (infoValues.Count >= 2 && DateTimeOffset.TryParseExact(infoValues[1], "MMM d, yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            releaseDate = parsed;

        return new AssetDetails(
            version,
            await GetApkUrls(standardUrl, cancellationToken),
            new AppNames(author, appName),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
