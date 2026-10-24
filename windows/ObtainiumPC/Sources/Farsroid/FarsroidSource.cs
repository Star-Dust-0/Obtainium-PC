using System.Net.Http;
using System.Text.Json;
using HtmlAgilityPack;
using ObtainiumPC.Sources.Html;

namespace ObtainiumPC.Sources.Farsroid;

/// <summary>
/// Port of lib/app_sources/farsroid.dart: post ID and version come from the
/// store page's download-links element; links come from the download-box API
/// and are filtered with the shared HTML link grabber. Track-only on Windows.
/// </summary>
public sealed class FarsroidSource : AppSourceBase
{
    private readonly HttpClient _http;
    private readonly HtmlSource _html = new();

    public FarsroidSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Farsroid";
    public override IReadOnlyList<string> Hosts => new[] { "farsroid.com" };
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"([^\.]+\.)", pathPattern: @"/[^/]+");

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appName = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        var res = await _http.GetAsync(standardUrl, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));
        var dlinks = html.DocumentNode.SelectNodes("//*[contains(concat(' ', normalize-space(@class), ' '), ' download-links ')]");
        if (dlinks == null || dlinks.Count == 0) throw new NoReleasesException();
        var postId = dlinks[0].GetAttributeValue("data-post-id", "");
        var version = dlinks[0].GetAttributeValue("data-post-version", "");
        if (postId.Length == 0 || version.Length == 0) throw new FDroid.NoVersionException();

        var apiUrl = $"https://{Hosts[0]}/api/download-box/?post_id={postId}&post_version={version}";
        var res2 = await _http.GetAsync(apiUrl, cancellationToken);
        if ((int)res2.StatusCode >= 400) throw new ObtainiumHttpException((int)res2.StatusCode, apiUrl);

        string content;
        try
        {
            using var doc = JsonDocument.Parse(await res2.Content.ReadAsStringAsync(cancellationToken));
            content = doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? "" : "";
        }
        catch (JsonException) { throw new NoAppAssetException(); }
        if (content.Length == 0) throw new NoAppAssetException();

        var settingsWithSkipSort = new Dictionary<string, JsonElement>(additionalSettings)
        {
            ["skipSort"] = JsonSerializer.SerializeToElement(true),
        };
        var apkLinks = _html.GrabLinksCommon(content, new Uri(apiUrl), settingsWithSkipSort)
            .Select(l => new KeyValuePair<string, string>(new Uri(l.Key).AbsolutePath.Split('/').Last(), l.Key))
            .ToList();

        var filterRegex = additionalSettings.TryGetValue("apkFilterRegEx", out var fr) && fr.ValueKind == JsonValueKind.String
            ? fr.GetString() : null;
        var invert = additionalSettings.TryGetValue("invertAPKFilter", out var inv) && inv.ValueKind == JsonValueKind.True;
        apkLinks = AssetFilterService.FilterAssets(apkLinks, filterRegex, invert ? true : null);
        if (apkLinks.Count == 0) throw new NoAppAssetException();

        if (additionalSettings.TryGetValue("useFirstApkOfVersion", out var first) && first.ValueKind == JsonValueKind.True)
            apkLinks = new List<KeyValuePair<string, string>> { apkLinks[0] };

        if (additionalSettings.TryGetValue("releaseTitleAsVersion", out var titleAsVersion) && titleAsVersion.ValueKind == JsonValueKind.True)
        {
            if (apkLinks.Count != 1) throw new FDroid.NoVersionException();
            version = apkLinks[0].Key;
        }

        return new AssetDetails(
            version,
            apkLinks,
            new AppNames(Name, appName),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
