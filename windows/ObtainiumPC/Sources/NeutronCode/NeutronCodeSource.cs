using System.Net.Http;
using System.Text.Json;
using HtmlAgilityPack;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.NeutronCode;

/// <summary>
/// Port of lib/app_sources/neutroncode.dart: single-file download pages on
/// neutroncode.com; version, filename, date and changelog scraped from the
/// page's pd-* elements.
/// </summary>
public sealed class NeutronCodeSource : AppSourceBase
{
    private static readonly IReadOnlyDictionary<string, string> MonthMap =
        new Dictionary<string, string>
        {
            ["january"] = "01", ["february"] = "02", ["march"] = "03", ["april"] = "04",
            ["may"] = "05", ["june"] = "06", ["july"] = "07", ["august"] = "08",
            ["september"] = "09", ["october"] = "10", ["november"] = "11", ["december"] = "12",
        };

    private readonly HttpClient _http;

    public NeutronCodeSource(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = ObtainiumHttp.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public override string Name => "NeutronCode";
    public override IReadOnlyList<string> Hosts => new[] { "neutroncode.com" };
    public override bool ShowReleaseDateAsVersionToggle => true;

    // Original: changeLogPageIsStandardUrl = true.
    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/downloads/file/[^/]+");

    private static string? FormatDateForParsing(string dateString)
    {
        var parts = dateString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return null;
        var monthIdx = Array.FindIndex(parts, s => !int.TryParse(s, out _));
        if (monthIdx < 0) return null;
        if (!MonthMap.TryGetValue(parts[monthIdx].ToLowerInvariant(), out var month)) return null;
        var numerics = parts.Where((_, i) => i != monthIdx)
            .Select(s => int.TryParse(s, out var n) ? n : -1).ToList();
        if (numerics.Count != 2 || numerics.Contains(-1)) return null;
        var (a, b) = (numerics[0], numerics[1]);
        var year = a > 31 ? a : b > 31 ? b : a.ToString().Length == 4 ? a : b;
        var day = a == year ? b : a;
        return $"{year}-{month}-{day:00}";
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var res = await _http.GetAsync(standardUrl, cancellationToken);
        if ((int)res.StatusCode != 200)
            throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);

        var doc = new HtmlDocument();
        doc.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));

        var name = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'pd-title')]")?.InnerHtml;
        var filename = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'pd-filename')]//*[contains(@class,'pd-float')]")?.InnerHtml?.Trim();
        if (filename == null) throw new NoReleasesException();

        var versionNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'pd-version-txt')]")?.NextSibling;
        while (versionNode != null && versionNode.NodeType != HtmlNodeType.Element) versionNode = versionNode.NextSibling;
        var version = versionNode?.InnerHtml?.Trim();
        if (string.IsNullOrEmpty(version)) throw new Services.Versioning.NoVersionException();

        var assetUrl = $"https://{Hosts[0]}/download/{filename}";

        var dateNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'pd-date-txt')]")?.NextSibling;
        while (dateNode != null && dateNode.NodeType != HtmlNodeType.Element) dateNode = dateNode.NextSibling;
        var dateString = dateNode?.InnerHtml is { } raw ? FormatDateForParsing(raw) : null;
        DateTimeOffset? releaseDate = dateString != null && DateTimeOffset.TryParse(dateString, out var parsed) ? parsed : null;

        var changeLogElements = doc.DocumentNode.SelectNodes("//*[contains(@class,'pd-fdesc')]//p");
        var changeLog = changeLogElements?.Count > 0 ? changeLogElements.Last().InnerHtml : null;

        return new AssetDetails(
            version,
            AssetFilterService.GetAssetUrlsFromUrls(new[] { assetUrl }),
            new AppNames(Name, name ?? standardUrl.Split('/').Last()),
            releaseDate,
            changeLog,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
