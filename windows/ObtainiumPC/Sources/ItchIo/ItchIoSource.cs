using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.ItchIo;

/// <summary>
/// Port of lib/app_sources/itchio.dart. itch.io uses a multi-step dynamic
/// download flow (CSRF token + cookies, itch.io's own "no thanks" free-download path, tokenized
/// Cloudflare R2 URLs). Windows mapping: the platform icon filter looks for
/// Windows uploads (icon-windows8) instead of Android ones.
/// </summary>
public sealed class ItchIoSource : AppSourceBase
{
    private readonly HttpClient _http;

    public ItchIoSource(HttpClient? http = null)
    {
        // No automatic redirects: the flow manages cookies across steps.
        _http = http ?? ObtainiumHttp.CreateClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "itch.io";
    public override IReadOnlyList<string> Hosts => new[] { "itch.io" };

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"[a-z0-9-]+\.", pathPattern: @"/[^/]+");

    private static string? FindCsrf(string body)
    {
        var match = Regex.Match(body, "name=\"csrf_token\" value=\"([^\"]+)\"");
        if (match.Success) return match.Groups[1].Value;
        match = Regex.Match(body, "csrf_token\":\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Name, upload ID, isWindows per downloadable upload on the page.
    private static List<(string Name, string Id, bool IsWindows)> ExtractDownloads(string body)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(body);
        var downloads = new List<(string, string, bool)>();
        foreach (var uploadDiv in doc.DocumentNode.SelectNodes("//div[contains(@class,'upload')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var uploadName = uploadDiv.SelectSingleNode(".//div[contains(@class,'upload_name')]//strong[contains(@class,'name')]")
                ?.GetAttributeValue("title", "App title") ?? "App title";
            var isWindows = uploadDiv.SelectSingleNode(".//span[contains(@class,'download_platforms')]//span[contains(@class,'icon-windows8')]") != null;
            var uploadId = uploadDiv.SelectSingleNode(".//a[contains(@class,'download_btn')]")?.GetAttributeValue("data-upload_id", null);
            if (uploadId != null) downloads.Add((uploadName, uploadId, isWindows));
        }
        return downloads;
    }

    private static string? ParseVersion(HtmlDocument document)
    {
        var pageWidget = document.DocumentNode.SelectSingleNode("//div[contains(@class,'page_widget')]");
        if (pageWidget == null) return null;
        var searchArea = pageWidget.InnerHtml;
        var matches = new HashSet<string>();
        foreach (var pattern in new[] { @"[vV](\d+\.\d+(?:\.\d+)*)", @"Version (\d+\.\d+(?:\.\d+)*)" })
            foreach (Match m in Regex.Matches(searchArea, pattern))
                matches.Add(m.Groups[1].Value);
        if (matches.Count == 0) return null;

        static int CompareVersions(string v1, string v2)
        {
            var c1 = v1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToList();
            var c2 = v2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToList();
            var maxLen = Math.Max(c1.Count, c2.Count);
            for (var i = 0; i < maxLen; i++)
            {
                var p1 = i < c1.Count ? c1[i] : 0;
                var p2 = i < c2.Count ? c2[i] : 0;
                if (p1 != p2) return p1.CompareTo(p2);
            }
            return 0;
        }
        return matches.Aggregate((a, b) => CompareVersions(a, b) > 0 ? a : b);
    }

    private static string? GetDateVersion(HtmlDocument document)
    {
        var abbrs = document.DocumentNode.SelectNodes("//abbr");
        if (abbrs == null || abbrs.Count == 0) return null;
        var dates = new List<DateTimeOffset>();
        foreach (var abbr in abbrs)
        {
            var title = abbr.GetAttributeValue("title", null);
            if (title == null) continue;
            if (DateTimeOffset.TryParseExact(title, "dd MMMM yyyy '@' HH:mm 'UTC'",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                dates.Add(parsed);
        }
        if (dates.Count == 0) return null;
        var latest = dates.Max();
        return $"{latest.Year}{latest.Month:00}{latest.Day:00}";
    }

    private static string ParseTitle(HtmlDocument document) =>
        (document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Split(" by ").First().Trim();

    private static string ParseAuthor(HtmlDocument document, string standardUrl)
    {
        var followSpan = document.DocumentNode.SelectSingleNode("//span[contains(@class,'on_follow')]//span[contains(@class,'full_label')]");
        if (followSpan != null)
        {
            var authorMatch = Regex.Match(followSpan.InnerText, @"Follow (.+)");
            if (authorMatch.Success) return authorMatch.Groups[1].Value.Trim();
        }
        return new Uri(standardUrl).Host.Split('.').First();
    }

    private async Task<(string? Csrf, string? Cookies)> SetupDownload(string standardUrl, CancellationToken ct)
    {
        var baseUrl = standardUrl.TrimEnd('/');
        var warmUp = await _http.GetAsync(baseUrl, ct);
        if ((int)warmUp.StatusCode != 200) return (null, null);
        var csrf = FindCsrf(await warmUp.Content.ReadAsStringAsync(ct));
        var cookies = warmUp.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join("; ", values) : null;
        return (csrf, cookies);
    }

    private async Task<string> GetDownloadPageBody(
        string standardUrl, string initialBody, string? csrfToken, string? cookies, CancellationToken ct)
    {
        var baseUrl = standardUrl.TrimEnd('/');
        if (ExtractDownloads(initialBody).Count > 0) return initialBody;

        // Follow itch.io's "No thanks, just take me to the downloads" path:
        // the same POST the web UI sends for free/pay-what-you-want downloads.
        // Paid-only uploads are never listed to this anonymous session.
        csrfToken ??= (await SetupDownload(standardUrl, ct)).Csrf;
        cookies ??= (await SetupDownload(standardUrl, ct)).Cookies;
        if (csrfToken == null || cookies == null) return initialBody;

        var bypass = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/download_url")
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("csrf_token", csrfToken) }),
        };
        bypass.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        bypass.Headers.TryAddWithoutValidation("Cookie", cookies);
        var bypassRes = await _http.SendAsync(bypass, ct);
        if ((int)bypassRes.StatusCode != 200) return initialBody;

        using var doc = JsonDocument.Parse(await bypassRes.Content.ReadAsStringAsync(ct));
        var tokenizedUrl = doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        if (tokenizedUrl == null) return initialBody;

        var downloadPage = new HttpRequestMessage(HttpMethod.Get, tokenizedUrl);
        downloadPage.Headers.TryAddWithoutValidation("Cookie", cookies);
        var downloadPageRes = await _http.SendAsync(downloadPage, ct);
        return (int)downloadPageRes.StatusCode == 200
            ? await downloadPageRes.Content.ReadAsStringAsync(ct)
            : initialBody;
    }

    private async Task<string?> RetrieveCloudflareUrl(
        string uploadId, string standardUrl, string? csrfToken, string? cookies, CancellationToken ct)
    {
        var baseUrl = standardUrl.TrimEnd('/');
        if (csrfToken == null || cookies == null)
            (csrfToken, cookies) = await SetupDownload(standardUrl, ct);
        if (csrfToken == null || cookies == null) return null;

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/file/{uploadId}?as_props=1&source=game_download")
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("csrf_token", csrfToken) }),
        };
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Referer", $"{baseUrl}/download/{uploadId}");
        req.Headers.TryAddWithoutValidation("Cookie", cookies);
        var res = await _http.SendAsync(req, ct);
        if ((int)res.StatusCode != 200) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
    }

    private async Task<string?> ResolveRealFileName(
        string uploadId, string standardUrl, string? csrfToken, string? cookies, CancellationToken ct)
    {
        var directUrl = await RetrieveCloudflareUrl(uploadId, standardUrl, csrfToken, cookies, ct);
        if (directUrl == null) return null;
        var req = new HttpRequestMessage(HttpMethod.Get, directUrl);
        req.Headers.TryAddWithoutValidation("Referer", standardUrl.TrimEnd('/') + "?download");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var cd = res.Content.Headers.ContentDisposition?.ToString()
            ?? (res.Content.Headers.TryGetValues("Content-Disposition", out var v) ? v.FirstOrDefault() : null);
        if (cd == null) return null;
        var match = Regex.Match(cd, "filename=\"?([^\";]+)\"?");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Resolves the short-lived Cloudflare R2 URL for the stored upload-ID link
    /// at download time. The signed URL is never persisted.
    public override async Task<string> AssetUrlPrefetchModifierAsync(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var uploadId = assetUrl.Split('/').Last();
        var cloudflareUrl = await RetrieveCloudflareUrl(uploadId, standardUrl, null, null, cancellationToken);
        return cloudflareUrl ?? assetUrl;
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = standardUrl.TrimEnd('/');
        var res = await _http.GetAsync(standardUrl, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        var csrfToken = FindCsrf(body);
        var cookies = res.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join("; ", values) : null;

        var storePage = new HtmlDocument();
        storePage.LoadHtml(body);
        var title = ParseTitle(storePage);
        var author = ParseAuthor(storePage, standardUrl);
        var dateVersion = GetDateVersion(storePage);
        var version = ParseVersion(storePage);

        var downloadPageBody = await GetDownloadPageBody(standardUrl, body, csrfToken, cookies, cancellationToken);
        var downloadPage = new HtmlDocument();
        downloadPage.LoadHtml(downloadPageBody);
        dateVersion ??= GetDateVersion(downloadPage);
        version ??= ParseVersion(downloadPage);

        // 1. Nice version, 2. last-update date, 3. 'latest'.
        version ??= dateVersion ?? "latest";

        var assetLinks = new List<KeyValuePair<string, string>>();
        foreach (var (name, id, isWindows) in ExtractDownloads(downloadPageBody))
        {
            if (!isWindows) continue;
            var realName = await ResolveRealFileName(id, standardUrl, csrfToken, cookies, cancellationToken);
            assetLinks.Add(new KeyValuePair<string, string>(realName ?? name, $"{baseUrl}/download/{id}"));
        }

        if (assetLinks.Count == 0) throw new NoAppAssetException();
        return new AssetDetails(
            version,
            assetLinks,
            new AppNames(author, title),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
