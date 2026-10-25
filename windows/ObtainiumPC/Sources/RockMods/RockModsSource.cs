using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.RockMods;

/// <summary>
/// Port of lib/app_sources/rockmods.dart. Track-only even upstream (PR
/// #2966): the site no longer exposes scrapeable downloads, so only the
/// version is tracked via JSON-LD.
/// </summary>
public sealed class RockModsSource : AppSourceBase
{
    private readonly HttpClient _http;

    public RockModsSource(HttpClient? http = null)
    {
        _http = http ?? ObtainiumHttp.CreateClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "RockMods";
    public override IReadOnlyList<string> Hosts => new[] { "rockmods.net" };
    public override bool SupportsInstall => false;
    public override bool NaiveStandardVersionDetection => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/apps/[^/]+");

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var res = await _http.GetAsync(standardUrl, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var body = await res.Content.ReadAsStringAsync(cancellationToken);

        string? appName = null, appVersion = null, appAuthor = null;
        foreach (Match m in Regex.Matches(body, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                if (doc.RootElement.TryGetProperty("@type", out var type) && type.GetString() == "SoftwareApplication")
                {
                    appName = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
                    appVersion = doc.RootElement.TryGetProperty("softwareVersion", out var v) ? v.GetString()?.Trim() : null;
                    if (doc.RootElement.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
                        && author.TryGetProperty("name", out var an))
                        appAuthor = an.GetString();
                    break;
                }
            }
            catch { /* malformed JSON-LD block - try the next */ }
        }

        if (string.IsNullOrEmpty(appName))
        {
            var html = new HtmlDocument();
            html.LoadHtml(body);
            appName = html.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim()
                ?? standardUrl.Split('/').Last();
        }
        if (string.IsNullOrEmpty(appVersion)) throw new FDroid.NoVersionException();

        return new AssetDetails(
            appVersion,
            new List<KeyValuePair<string, string>>(),
            new AppNames(appAuthor ?? Name, appName),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
