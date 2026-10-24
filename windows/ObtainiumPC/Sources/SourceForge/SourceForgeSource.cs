using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ObtainiumPC.Services.Versioning;

namespace ObtainiumPC.Sources.SourceForge;

/// <summary>
/// Port of lib/app_sources/sourceforge.dart: latest release from the project's
/// RSS feed, version extracted from path segments after stripping the filename
/// and optionally a subdirectory.
/// </summary>
public sealed class SourceForgeSource : AppSourceBase
{
    private readonly HttpClient _http;

    public SourceForgeSource(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public override string Name => "SourceForge";
    public override IReadOnlyList<string> Hosts => new[] { "sourceforge.net" };
    public override bool SuppressStandardVersionExtraction => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var sourceRegex = GetSourceRegex(Hosts);
        var matchC = Regex.Match(url, "^https?://(www\\.)?" + sourceRegex + "/p/.+", RegexOptions.IgnoreCase);
        if (matchC.Success)
        {
            var parsed = new Uri(matchC.Value);
            var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var project = segments.Length > 1 ? segments[1] : "";
            url = $"https://{parsed.Host}/projects/{project}";
        }
        var matchB = Regex.Match(url, "^https?://(www\\.)?" + sourceRegex + "/projects/[^/]+", RegexOptions.IgnoreCase);
        if (matchB.Success && matchB.Value == url) url = url + "/files";
        var matchA = Regex.Match(url, "^https?://(www\\.)?" + sourceRegex + @"/projects/[^/]+/files(/.+)?", RegexOptions.IgnoreCase);
        if (!matchA.Success) throw new InvalidUrlException(Name);
        return matchA.Value;
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var standardUri = new Uri(standardUrl);
        var pathSegments = standardUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 2)
        {
            standardUrl += "/files";
            standardUri = new Uri(standardUrl);
            pathSegments = standardUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
        var rssUrl = $"{standardUri.Scheme}://{standardUri.Host}/{string.Join('/', pathSegments.Take(2))}/rss?path=/";
        var res = await _http.GetAsync(rssUrl, cancellationToken);
        if ((int)res.StatusCode != 200)
            throw new ObtainiumHttpException((int)res.StatusCode, rssUrl);

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        XDocument doc;
        try { doc = XDocument.Parse(body); }
        catch (System.Xml.XmlException) { throw new NoReleasesException(); }

        var allDownloadLinks = doc.Descendants()
            .Where(e => e.Name.LocalName == "guid")
            .Select(e => e.Value)
            .Where(link => link.StartsWith(standardUrl, StringComparison.Ordinal))
            .ToList();

        string? GetVersion(string url)
        {
            try
            {
                var segments = url.Substring(standardUrl.Length).Split('/')
                    .Where(el => el.Length > 0).ToList();
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                if (segments.Count > 1) segments.RemoveAt(segments.Count - 1);
                string? version = segments.Count > 0 ? string.Join('/', segments) : null;
                if (version != null)
                {
                    try
                    {
                        var extracted = VersionService.ExtractVersion(
                            GetString(additionalSettings, "versionExtractionRegEx"),
                            GetString(additionalSettings, "matchGroupToUse"),
                            version);
                        if (extracted != null) version = extracted;
                    }
                    catch (NoVersionException) { version = null; }
                }
                return version;
            }
            catch (FormatException) { return null; }
            catch (NoVersionException) { return null; }
        }

        var releasesWithVersions = allDownloadLinks
            .Where(link =>
            {
                var lower = link.ToLowerInvariant();
                const string suffix = "/download";
                return lower.EndsWith(suffix)
                    && AssetFilterService.IsAppOrContainerFile(lower.Substring(0, lower.Length - suffix.Length));
            })
            .Select(link => new KeyValuePair<string, string?>(link, GetVersion(link)))
            .Where(entry => entry.Value != null)
            .ToList();

        if (releasesWithVersions.Count == 0) throw new NoReleasesException();
        var version = releasesWithVersions[0].Value;
        if (string.IsNullOrEmpty(version)) throw new NoVersionException();

        var assetUrlList = releasesWithVersions
            .Where(entry => entry.Value == version)
            .Select(entry => entry.Key)
            .ToList();
        var urlSegments = standardUrl.Split('/');
        var filesIndex = Array.IndexOf(urlSegments, "files");
        var projectName = filesIndex > 0 ? urlSegments[filesIndex - 1] : Name;

        return new AssetDetails(
            version,
            AssetFilterService.GetAssetUrlsFromUrls(assetUrlList),
            new AppNames(Name, projectName),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
