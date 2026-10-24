using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ObtainiumPC.Sources.Jenkins;

/// <summary>
/// Port of lib/app_sources/jenkins.dart: artifacts of a job's last successful
/// build. Version is the build number; release date is the build timestamp.
/// Jenkins jobs can produce real Windows installers, so installs are allowed.
/// </summary>
public sealed class JenkinsSource : AppSourceBase
{
    private readonly HttpClient _http;

    public JenkinsSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Jenkins";
    public override IReadOnlyList<string> Hosts => Array.Empty<string>(); // any host with /job/<name>
    public override bool VersionDetectionDisallowed => true;
    public override bool NeverAutoSelect => true;

    public static bool LooksLikeJenkinsJobUrl(string url) =>
        Regex.IsMatch(url, @"https?://[^/]+/job/[^/]+", RegexOptions.IgnoreCase);

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var match = Regex.Match(url, @"https?://[^/]+/job/[^/]+", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidUrlException(Name);
        return match.Value;
    }

    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl;

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        standardUrl = SourceSpecificStandardizeUrl(standardUrl);
        var res = await _http.GetAsync($"{standardUrl}/lastSuccessfulBuild/api/json", cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;

        DateTimeOffset? releaseDate = null;
        if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var ms))
            releaseDate = DateTimeOffset.FromUnixTimeMilliseconds(ms);

        var version = root.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number
            ? number.GetInt32().ToString()
            : null;
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();

        var assetUrls = new List<KeyValuePair<string, string>>();
        if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var artifact in artifacts.EnumerateArray())
            {
                var relativePath = artifact.TryGetProperty("relativePath", out var rp) && rp.ValueKind == JsonValueKind.String
                    ? rp.GetString() : null;
                if (string.IsNullOrEmpty(relativePath)) continue;
                var fileName = artifact.TryGetProperty("fileName", out var fn) && fn.ValueKind == JsonValueKind.String
                    ? fn.GetString()! : relativePath;
                if (!AssetFilterService.IsAppOrContainerFile(fileName, includeArchives: true, includeTarballs: true)) continue;
                assetUrls.Add(new KeyValuePair<string, string>(
                    fileName,
                    $"{standardUrl}/lastSuccessfulBuild/artifact/{relativePath}"));
            }
        }

        return new AssetDetails(
            version,
            assetUrls,
            new AppNames(new Uri(standardUrl).Host, standardUrl.Split('/').Last()),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(assetUrls),
            null,
            null);
    }
}
