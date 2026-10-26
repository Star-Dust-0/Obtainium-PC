using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.GitLab;

public sealed record GitLabSourceConfig(
    string? PersonalAccessToken = null,
    int GlobalMinimumUpdateAgeDays = 0);

/// <summary>
/// Port of lib/app_sources/gitlab.dart. Windows mapping: "isApkAsset" accepts
/// Windows installer/container extensions, and package links whose name or URL
/// carries an installer token (exe/msi/msix/appx/setup/installer) the same way
/// the original accepts APK tokens.
/// </summary>
public sealed class GitLabSource : AppSourceBase
{
    private readonly GitLabSourceConfig _config;
    private readonly HttpClient _http;

    public GitLabSource(GitLabSourceConfig? config = null, HttpClient? http = null)
    {
        _config = config ?? new GitLabSourceConfig();
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = ObtainiumHttp.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public override string Name => "GitLab";
    public override IReadOnlyList<string> Hosts => new[] { "gitlab.com" };

    // lib/app_sources/gitlab.dart search
    public override bool CanSearch => true;

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query, IReadOnlyDictionary<string, JsonElement> querySettings, CancellationToken ct = default)
    {
        var url = $"https://{Hosts[0]}/api/v4/projects?search=" + Uri.EscapeDataString(query);
        var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        var results = new Dictionary<string, IReadOnlyList<string>>();
        var arr = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))?.AsArray();
        if (arr == null) return results;
        foreach (var e in arr)
        {
            var path = e?["path_with_namespace"]?.GetValue<string>();
            if (path == null) continue;
            var name = e?["name_with_namespace"]?.GetValue<string>() ?? path;
            var desc = e?["description"]?.GetValue<string>() ?? "No description";
            results[$"https://{Hosts[0]}/" + path] = new List<string> { name, desc };
        }
        return results;
    }
    public override bool ShowReleaseDateAsVersionToggle => true;

    /// Fallback-to-older-releases is GitLab's only per-app release option upstream.
    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release selection", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
        };

    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl + "/-/releases";

    public static bool IsInstallableAsset(string name, string url, string? linkType)
    {
        if (AssetFilterService.IsAppOrContainerFile(name) || AssetFilterService.IsAppOrContainerFile(url))
            return true;
        if (linkType != "package") return false;
        var token = new Regex(@"(^|[^a-z])(exe|msi|msix|appx|setup|installer)([^a-z]|$)", RegexOptions.IgnoreCase);
        return token.IsMatch(name) || token.IsMatch(url);
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var segments = url.Split('/');
        var cutOff = Array.IndexOf(segments, "-");
        if (cutOff > 0) url = string.Join('/', segments.Take(cutOff));
        var re = new Regex("^https?://(www\\.)?" + GetSourceRegex(Hosts) + @"/[^/]+(/[^/]+){1,20}", RegexOptions.IgnoreCase);
        var match = re.Match(url);
        if (!match.Success) throw new InvalidUrlException(Name);
        return match.Value;
    }

    /// Same path parsing the original gets from its GitHub helper: author is the
    /// first path segment, the project is the remaining joined segments (subgroups).
    public AppNames GetAppNames(string standardUrl)
    {
        var temp = standardUrl.Substring(standardUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        var pathStart = temp.IndexOf('/');
        if (pathStart < 0) throw new InvalidUrlException(Name);
        var names = temp.Substring(pathStart + 1).Split('/');
        if (names.Length == 0 || names[0].Length == 0) throw new InvalidUrlException(Name);
        return new AppNames(names[0], string.Join('/', names.Skip(1)));
    }

    /// Per-app 'gitlab-creds' overrides the global GitLab token, as upstream's
    /// getSourceConfigValues prefers per-app source config values.
    private string? GetPat(IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        if (additionalSettings.TryGetValue("gitlab-creds", out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString() is { Length: > 0 } perApp)
            return perApp;
        return string.IsNullOrEmpty(_config.PersonalAccessToken) ? null : _config.PersonalAccessToken;
    }

    public override string AssetUrlPrefetchModifier(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var pat = GetPat(additionalSettings);
        if (pat == null) return assetUrl;
        return assetUrl + (assetUrl.Contains('?') ? "&" : "?") + "private_token=" + pat;
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var names = GetAppNames(standardUrl);
        var projectUriComponent = Uri.EscapeDataString(names.Author) + "%2F" + Uri.EscapeDataString(names.Name);
        var pat = GetPat(additionalSettings);
        var optionalAuth = pat != null ? "private_token=" + pat : "";
        var trackOnly = GetBool(additionalSettings, "trackOnly");

        // Project ID
        var res0 = await _http.GetAsync(
            $"https://{Hosts[0]}/api/v4/projects/{projectUriComponent}?{optionalAuth}", cancellationToken);
        await EnsureSuccess(res0);
        using var doc0 = await JsonDocument.ParseAsync(await res0.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (doc0.RootElement.ValueKind != JsonValueKind.Object
            || !doc0.RootElement.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
            throw new NoReleasesException();
        var projectId = idProp.GetInt64();
        bool? projectArchived = doc0.RootElement.TryGetProperty("archived", out var archProp) && archProp.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? archProp.GetBoolean() : null;

        // Releases (or tags when track-only)
        var releasesPath = trackOnly ? "repository/tags" : "releases";
        var query = (optionalAuth.Length > 0 ? optionalAuth + "&" : "") + "per_page=100";
        var res = await _http.GetAsync(
            $"https://{Hosts[0]}/api/v4/projects/{projectUriComponent}/{releasesPath}?{query}", cancellationToken);
        await EnsureSuccess(res);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new NoReleasesException();

        var detailsList = new List<AssetDetails>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var assetUrls = new Dictionary<string, string>();

            if (e.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object
                && assets.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in links.EnumerateArray())
                {
                    var linkName = link.TryGetProperty("name", out var ln) && ln.ValueKind == JsonValueKind.String ? ln.GetString() ?? "" : "";
                    var linkUrl = link.TryGetProperty("direct_asset_url", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() ?? ""
                        : link.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
                    var linkType = link.TryGetProperty("link_type", out var lt) && lt.ValueKind == JsonValueKind.String ? lt.GetString() : null;
                    if (!IsInstallableAsset(linkName, linkUrl, linkType)) continue;
                    var key = linkName.Length > 0 ? linkName
                        : Uri.TryCreate(linkUrl, UriKind.Absolute, out var parsed) && parsed.Segments.Length > 0
                            ? parsed.Segments[^1] : "unknown";
                    if (key.Length > 0) assetUrls[key] = linkUrl;
                }
            }

            // Assets linked from the release description (uploaded files)
            var description = e.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() ?? "" : "";
            description = description.Replace("](", "\n");
            foreach (var ext in AssetFilterService.AppContainerExtensions)
                description = description.Replace(ext + ")", ext + "\n");
            foreach (var line in description.Split('\n'))
            {
                var s = line.Trim();
                if (!s.StartsWith("/uploads/") || !AssetFilterService.IsAppOrContainerFile(s)) continue;
                var full = $"https://{Hosts[0]}/-/project/{projectId}{s}";
                var segName = full.Split('/')[^1];
                assetUrls[segName] = full;
            }

            var version = e.TryGetProperty("tag_name", out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString()
                : e.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : null;
            if (version == null) continue;

            DateTimeOffset? releaseDate = null;
            foreach (var dateKey in new[] { "released_at", "created_at" })
            {
                if (releaseDate != null) break;
                if (e.TryGetProperty(dateKey, out var dp) && dp.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(dp.GetString(), out var parsed)) releaseDate = parsed;
            }
            if (releaseDate == null
                && e.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object
                && commit.TryGetProperty("created_at", out var cd) && cd.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(cd.GetString(), out var commitDate)) releaseDate = commitDate;

            detailsList.Add(new AssetDetails(
                version,
                assetUrls.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)).ToList(),
                new AppNames(names.Author, names.Name.Split('/').Last()),
                releaseDate,
                null,
                new List<KeyValuePair<string, string>>(),
                null,
                null));
        }

        if (detailsList.Count == 0) throw new NoReleasesException();

        // Prefer the newest release old enough for the minimum update age; if
        // none is, keep the newest so the provider can suppress it until it ages.
        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") is { } perAppAge && perAppAge >= 0
            ? perAppAge : _config.GlobalMinimumUpdateAgeDays;
        if (minAgeDays > 0)
        {
            var eligible = detailsList.Where(d => !IsReleaseTooYoung(d.ReleaseDate, minAgeDays)).ToList();
            if (eligible.Count > 0) detailsList = eligible;
        }

        var finalResult = detailsList[0];

        if (finalResult.AssetUrls.Count == 0 && GetBool(additionalSettings, "fallbackToOlderReleases") && !trackOnly)
        {
            var withAssets = detailsList.Where(d => d.AssetUrls.Count > 0).ToList();
            if (withAssets.Count == 0) throw new NoReleasesException();
            finalResult = withAssets[0];
        }

        if (finalResult.AssetUrls.Count == 0 && !trackOnly)
            throw new NoAppAssetException { ReleaseAssets = finalResult.AllAssetUrls };

        // Job artifact links serve raw content under /raw/ instead of /file/.
        var jobArtifactRegex = new Regex("^" + Regex.Escape(standardUrl) + @"/-/jobs/[0-9]+/artifacts/file/[^/]+");
        var fixedUrls = finalResult.AssetUrls.Select(a => jobArtifactRegex.IsMatch(a.Value)
            ? new KeyValuePair<string, string>(a.Key, a.Value.Replace("/file/", "/raw/"))
            : a).ToList();

        return finalResult with { AssetUrls = fixedUrls, IsArchived = projectArchived };
    }

    private static async Task EnsureSuccess(HttpResponseMessage res)
    {
        if ((int)res.StatusCode >= 400)
            throw new ObtainiumHttpException((int)res.StatusCode, res.RequestMessage?.RequestUri?.ToString() ?? "");
        await Task.CompletedTask;
    }
}
