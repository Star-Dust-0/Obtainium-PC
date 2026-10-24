using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;

namespace ObtainiumPC.Sources.Uptodown;

/// <summary>
/// Port of lib/app_sources/uptodown.dart: Uptodown store via page scraping plus
/// the uptodown.app eAPI (HMAC-signed anonymous session) for download URLs.
/// Track-only on Windows.
/// </summary>
public sealed class UptodownSource : AppSourceBase
{
    private const string ApiHost = "www.uptodown.app";
    private const string AuthPath = "/eapi/auth/token";
    private const string SearchUrl = "https://en.uptodown.com/android/en/s";
    private const string ClientVersion = "739";
    private const string UserAgent =
        "Dalvik/2.1.0 (Linux; U; Android 16; Pixel 8 Pro Build/BP4A.260205.001)";
    private const string HmacKey = "MDGMXUMdvHJBG/vjdFgmqX6LUdy7ecfwvYNd0gyfOCs=";

    private static readonly (string Fragment, string Label)[] IconLabels =
    {
        ("icon-40-package", "package name"),
        ("icon-40-date", "date"),
        ("icon-40-type", "file type"),
    };

    private readonly HttpClient _http;
    private (string Token, long ExpiresAt)? _session;

    public UptodownSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public override string Name => "Uptodown";
    public override IReadOnlyList<string> Hosts => new[] { "uptodown.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;
    public override bool ShowReleaseDateAsVersionToggle => true;
    public override bool CanSearch => true;

    private HttpRequestMessage BuildApiRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        var uri = new Uri(url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Identificador", "Uptodown_Android");
        req.Headers.TryAddWithoutValidation("Identificador-Version", ClientVersion);
        if (uri.AbsolutePath != AuthPath && _session is { } session)
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session.Token);
        return req;
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var standardized = StandardizeUrlWithRegex(url, @"([^\.]+\.)+", "");
        var uri = new Uri(standardized);
        // uri.replace(host: '<first label>.en.uptodown.com', path: '/android/download')
        return $"https://{uri.Host.Split('.')[0]}.en.uptodown.com/android/download{uri.Query}";
    }

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement> querySettings,
        CancellationToken cancellationToken = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, SearchUrl)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("queryString", query) }),
        };
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        var res = await _http.SendAsync(req, cancellationToken);
        if ((int)res.StatusCode != 200) throw new ObtainiumHttpException((int)res.StatusCode, SearchUrl);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("success", out var success)
            || success.GetInt32() != 1)
            throw new ObtainiumException("Uptodown search failed.");
        var results = new Dictionary<string, IReadOnlyList<string>>();
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("apps", out var apps)
            && apps.ValueKind == JsonValueKind.Array)
        {
            foreach (var app in apps.EnumerateArray())
            {
                if (app.ValueKind != JsonValueKind.Object
                    || !app.TryGetProperty("platformURL", out var platform)
                    || platform.GetString() != "/android")
                    continue;
                var url = app.TryGetProperty("url", out var u) ? u.ToString() : null;
                var name = app.TryGetProperty("name", out var n)
                    ? System.Text.RegularExpressions.Regex.Replace(n.ToString(), "<[^>]+>", "").Trim()
                    : null;
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) continue;
                var author = app.TryGetProperty("author", out var a) ? a.ToString().Trim() : "";
                results[url] = new List<string> { name, author.Length > 0 ? author : "No description" };
            }
        }
        return results;
    }

    private static string? TextOfId(HtmlDocument html, string id) =>
        html.GetElementbyId(id)?.InnerText?.Trim();

    private async Task<Dictionary<string, string?>> GetAppDetailsFromPageAsync(
        string standardUrl, CancellationToken cancellationToken)
    {
        using var res = await _http.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, standardUrl), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));

        var version = html.DocumentNode
            .SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' version ')]")
            ?.InnerText?.Trim();
        var appNameElement = html.GetElementbyId("detail-app-name");
        var name = appNameElement?.InnerText?.Trim();
        var author = TextOfId(html, "author-link");

        // Technical information by label; fall back to icon fragments when
        // labels are missing or still localized.
        var info = new Dictionary<string, string>();
        var rows = html.DocumentNode.SelectNodes("//*[@id='technical-information']//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var label = row.SelectSingleNode(".//th")?.InnerText?.Trim().ToLowerInvariant();
                if (label == null || !IconLabels.Any(il => il.Label == label))
                {
                    var src = row.SelectSingleNode(".//td/img")?.GetAttributeValue("src", "");
                    var fragment = src is { Length: > 0 } && src.Contains('#')
                        ? src.Substring(src.LastIndexOf('#') + 1)
                        : null;
                    label = IconLabels.FirstOrDefault(il => il.Fragment == fragment).Label;
                    if (label == null) continue;
                }
                var cells = row.SelectNodes(".//td");
                var value = cells is { Count: > 0 } ? cells[cells.Count - 1].InnerText.Trim() : null;
                if (!string.IsNullOrEmpty(value)) info[label] = value;
            }
        }

        info.TryGetValue("package name", out var appId);
        info.TryGetValue("date", out var dateStr);
        info.TryGetValue("file type", out var fileType);
        var fileId = html.GetElementbyId("detail-download-button")?.GetAttributeValue("data-file-id", null)
            ?? appNameElement?.GetAttributeValue("data-file-id", null);

        return new Dictionary<string, string?>
        {
            ["version"] = version,
            ["appId"] = appId,
            ["name"] = name,
            ["author"] = author,
            ["dateStr"] = dateStr,
            ["fileId"] = fileId,
            ["extension"] = fileType?.ToLowerInvariant(),
        };
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appDetails = await GetAppDetailsFromPageAsync(standardUrl, cancellationToken);
        var version = appDetails["version"];
        var appId = appDetails["appId"];
        var fileId = appDetails["fileId"];
        var extension = appDetails["extension"];
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
        if (fileId == null || (extension != "apk" && extension != "xapk")) throw new NoAppAssetException();
        var apkUrl = $"{standardUrl}/{fileId}-x";
        if (appId == null) throw new NoReleasesException();
        var appName = appDetails["name"] ?? "App";
        var author = appDetails["author"] ?? Name;
        var relDate = ParseUptodownDate(appDetails["dateStr"]);
        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new($"{appId}.{extension}", apkUrl) },
            new AppNames(author, appName),
            relDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    public override async Task<string> AssetUrlPrefetchModifierAsync(
        string assetUrl,
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        using var res = await _http.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, assetUrl), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, assetUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));
        var button = html.GetElementbyId("detail-download-button");
        var heading = html.GetElementbyId("detail-app-name");
        var appId = button?.GetAttributeValue("data-app-id", null)
            ?? heading?.GetAttributeValue("data-code", null);
        var fileId = button?.GetAttributeValue("data-file-id", null)
            ?? heading?.GetAttributeValue("data-file-id", null);
        if (appId == null || fileId == null) throw new NoAppAssetException();
        return await ResolveDownloadAsync(appId, fileId, cancellationToken);
    }

    private async Task<(string Token, long ExpiresAt)> GetSessionAsync(
        bool forceRefresh, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!forceRefresh && _session is { } cached && now < cached.ExpiresAt - 60)
            return cached;
        var session = await AuthAsync(cancellationToken);
        _session = session;
        return session;
    }

    private async Task<(string Token, long ExpiresAt)> AuthAsync(CancellationToken cancellationToken)
    {
        var random = Random.Shared;
        var identifier = string.Concat(Enumerable.Range(0, 8).Select(_ => random.Next(256).ToString("x2")));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacKey));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp))).ToLowerInvariant();

        var url = $"https://{ApiHost}{AuthPath}?identifier={identifier}";
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("identifier", identifier),
            new KeyValuePair<string, string>("id_plataforma", "13"),
            new KeyValuePair<string, string>("lang", "en"),
            new KeyValuePair<string, string>("unixtime", timestamp),
            new KeyValuePair<string, string>("hmac", signature),
        });
        using var res = await _http.SendAsync(BuildApiRequest(HttpMethod.Post, url, content), cancellationToken);
        if ((int)res.StatusCode != 200) throw new ObtainiumHttpException((int)res.StatusCode, url);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
        var token = doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        if (token == null || token.Split('.').Length != 3)
            throw new ObtainiumException("Uptodown returned an invalid auth response.");
        var claims = JsonDocument.Parse(Base64UrlDecodeToString(token.Split('.')[1]));
        if (!claims.RootElement.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expiresAt))
            throw new ObtainiumException("Uptodown returned an invalid auth response.");
        return (token, expiresAt);
    }

    private async Task<string> ResolveDownloadAsync(
        string appId, string fileId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await GetSessionAsync(forceRefresh: attempt > 0, cancellationToken);
            var url = $"https://{ApiHost}/eapi/apps/{appId}/file/{fileId}/downloadUrl";
            using var res = await _http.SendAsync(BuildApiRequest(HttpMethod.Get, url), cancellationToken);
            if ((int)res.StatusCode == 401) continue;
            if ((int)res.StatusCode != 200) throw new ObtainiumHttpException((int)res.StatusCode, url);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("success", out var success)
                || success.GetInt32() != 1)
                throw new ObtainiumException("Uptodown download resolution failed.");
            string? downloadUrl = null;
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("downloadURL", out var d))
                downloadUrl = d.GetString();
            if (downloadUrl == null) throw new NoAppAssetException();
            return downloadUrl;
        }
        throw new ObtainiumException("Uptodown download resolution failed.");
    }

    private static string Base64UrlDecodeToString(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    internal static DateTimeOffset? ParseUptodownDate(string? dateString)
    {
        if (dateString == null) return null;
        foreach (var format in new[] { "MMM dd, yyyy", "MMMM dd, yyyy" })
        {
            if (DateTimeOffset.TryParseExact(dateString, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;
        }
        return null;
    }
}
