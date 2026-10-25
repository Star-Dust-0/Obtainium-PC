using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using HtmlAgilityPack;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.SourceHut;

/// <summary>
/// Port of lib/app_sources/sourcehut.dart: release refs from the project's RSS
/// feed, assets scraped from each ref page.
/// </summary>
public sealed class SourceHutSource : AppSourceBase
{
    private readonly HttpClient _http;

    public SourceHutSource(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = ObtainiumHttp.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public override string Name => "SourceHut";
    public override IReadOnlyList<string> Hosts => new[] { "git.sr.ht" };
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release selection", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
        };

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/[^/]+/[^/]+");

    // Original: changeLogPageIsStandardUrl = true.
    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl;

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        if (standardUrl.EndsWith("/refs"))
        {
            var urlParts = standardUrl.Split('/');
            standardUrl = string.Join('/', urlParts.Take(urlParts.Length - 1));
        }
        var standardUri = new Uri(standardUrl);
        var appName = standardUri.Segments[^1].TrimEnd('/');
        var fallbackToOlderReleases = GetBool(additionalSettings, "fallbackToOlderReleases");
        var trackOnly = GetBool(additionalSettings, "trackOnly");
        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") ?? 0;

        var rssUrl = standardUrl + "/refs/rss.xml";
        var res = await _http.GetAsync(rssUrl, cancellationToken);
        if ((int)res.StatusCode != 200)
            throw new ObtainiumHttpException((int)res.StatusCode, rssUrl);

        XDocument doc;
        try { doc = XDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken)); }
        catch (System.Xml.XmlException) { throw new NoReleasesException(); }

        var detailsList = new List<AssetDetails>();
        var ind = 0;
        var releaseSkipped = 0;
        foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "item").Take(6))
        {
            ind++;
            var releasePage = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "guid")?.Value.Trim() ?? "";
            if (!releasePage.StartsWith(standardUrl + "/refs")) continue;
            if (!fallbackToOlderReleases && ind > releaseSkipped + 1) break;

            var version = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
            if (string.IsNullOrEmpty(version)) throw new Services.Versioning.NoVersionException();

            DateTimeOffset? releaseDate = null;
            var releaseDateString = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "pubDate")?.Value;
            if (releaseDateString != null
                && DateTimeOffset.TryParseExact(releaseDateString, "ddd, dd MMM yyyy HH:mm:ss zzz",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                releaseDate = parsed;

            if (IsReleaseTooYoung(releaseDate, minAgeDays)) releaseSkipped++;

            var assetUrls = new List<KeyValuePair<string, string>>();
            using var res2 = await _http.GetAsync(releasePage, cancellationToken);
            if ((int)res2.StatusCode == 200)
            {
                var page = new HtmlDocument();
                page.LoadHtml(await res2.Content.ReadAsStringAsync(cancellationToken));
                var hrefs = (page.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
                    .Select(a => a.GetAttributeValue("href", ""))
                    .Where(h => AssetFilterService.IsAppOrContainerFile(h))
                    .Select(h => Html.HtmlSource.EnsureAbsoluteUrl(h, standardUri))
                    .ToList();
                assetUrls = AssetFilterService.GetAssetUrlsFromUrls(hrefs);
            }

            var author = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "author")?.Value.Trim();
            detailsList.Add(new AssetDetails(
                version,
                assetUrls,
                new AppNames(string.IsNullOrEmpty(author) ? appName : author, appName),
                releaseDate,
                null,
                new List<KeyValuePair<string, string>>(),
                null,
                null));
        }

        if (detailsList.Count == 0) throw new NoReleasesException();

        // Prefer the newest release old enough for the minimum update age.
        if (minAgeDays > 0)
        {
            var eligible = detailsList.Where(d => !IsReleaseTooYoung(d.ReleaseDate, minAgeDays)).ToList();
            if (eligible.Count > 0) detailsList = eligible;
        }
        if (fallbackToOlderReleases)
        {
            if (!trackOnly)
                detailsList = detailsList.Where(d => d.AssetUrls.Count > 0).ToList();
            if (detailsList.Count == 0) throw new NoReleasesException();
        }
        return detailsList[0];
    }
}
