using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ObtainiumPC.Sources.LiteApks;

/// <summary>
/// Port of lib/app_sources/liteapks.dart: WordPress API two-step (slug to
/// post, post to versions). Downloads need a Referer header (the store page)
/// and a time-based double-base64 CDN token added at download time.
/// Track-only on Windows.
/// </summary>
public sealed class LiteApksSource : AppSourceBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(3);

    private readonly HttpClient _http;

    public LiteApksSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "LiteAPKs";
    public override IReadOnlyList<string> Hosts => new[] { "liteapks.com" };
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/+[^/]+");

    // The Referer is the store page, stowed after '#' on the asset URL.
    public override IReadOnlyDictionary<string, string>? GetDownloadRequestHeaders(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings) =>
        new Dictionary<string, string> { ["Referer"] = assetUrl.Split('#').Last() };

    // Generates the time-based CDN token the server expects (now + 3h).
    public override string AssetUrlPrefetchModifier(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        string B64(string input) => Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        var expiry = (DateTimeOffset.Now.ToUnixTimeSeconds() + (long)CacheDuration.TotalSeconds).ToString();
        var token = B64(B64(expiry)).Replace("=", "%3D");
        var parts = assetUrl.Split('#');
        parts[0] = $"{parts[0]}?token={token}";
        return string.Join('#', parts);
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var standardUri = new Uri(standardUrl);
        var slug = standardUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last().Split('.').First();
        var origin = $"{standardUri.Scheme}://{standardUri.Host}";

        var res1 = await _http.GetAsync($"{origin}/wp-json/wp/v2/posts?slug={slug}", cancellationToken);
        if ((int)res1.StatusCode >= 400) throw new ObtainiumHttpException((int)res1.StatusCode, standardUrl);
        using (var postsDoc = JsonDocument.Parse(await res1.Content.ReadAsStringAsync(cancellationToken)))
        {
            if (postsDoc.RootElement.ValueKind != JsonValueKind.Array || postsDoc.RootElement.GetArrayLength() == 0)
                throw new NoReleasesException();
            if (!postsDoc.RootElement[0].TryGetProperty("id", out var idEl))
                throw new NoReleasesException();

            var res2 = await _http.GetAsync($"{origin}/wp-json/v2/posts/{idEl}", cancellationToken);
            if ((int)res2.StatusCode >= 400) throw new ObtainiumHttpException((int)res2.StatusCode, standardUrl);
            using var doc = JsonDocument.Parse(await res2.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new NoReleasesException();

            var appName = data.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var author = data.TryGetProperty("publisher", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            string? version = null;
            var apkUrls = new List<KeyValuePair<string, string>>();
            if (data.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array
                && versions.GetArrayLength() > 0)
            {
                var first = versions[0];
                version = first.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (first.TryGetProperty("version_downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dl in downloads.EnumerateArray())
                    {
                        if (!dl.TryGetProperty("version_download_link", out var link) || link.ValueKind != JsonValueKind.String)
                            continue;
                        var url = link.GetString();
                        if (string.IsNullOrEmpty(url)) continue;
                        var segments = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        var filename = segments.Length > 0 ? Uri.UnescapeDataString(segments.Last()) : url.Split('/').Last(s => s.Length > 0);
                        apkUrls.Add(new KeyValuePair<string, string>(filename, $"{url}#{standardUrl}"));
                    }
                }
            }
            if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();

            return new AssetDetails(
                version,
                apkUrls,
                new AppNames(author ?? standardUri.Host, appName ?? standardUrl.Split('/').Last()),
                null,
                null,
                new List<KeyValuePair<string, string>>(),
                null,
                null);
        }
    }
}
