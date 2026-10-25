using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ObtainiumPC.Services.Versioning;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.GitHub;

/// Per-source host configuration (the original source-config UI values).
public sealed record GitHubSourceConfig(
    string? PersonalAccessToken = null,
    string? GhReqPrefix = null,
    bool CheckRepoRename = true,
    bool UpdateHealthArchivedEnabled = true,
    int GlobalMinimumUpdateAgeDays = 0,
    string ApiHost = "https://api.github.com");

/// <summary>
/// Port of lib/app_sources/github.dart: release fetching, sorting, filtering,
/// pre-release handling, latest-tag verification, min-age deferral, repo rename
/// detection, and token auth with a no-auth retry.
/// </summary>
public sealed class GitHubSource : AppSourceBase
{
    private readonly GitHubSourceConfig _config;
    private readonly HttpClient _http;

    public GitHubSource(GitHubSourceConfig? config = null, HttpClient? http = null)
    {
        _config = config ?? new GitHubSourceConfig();
        _http = http ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = ObtainiumHttp.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public override bool ShowReleaseDateAsVersionToggle => true;
    public override bool AllowIncludeZips => true;
    public override bool AllowIncludeTarballs => true;

    /// Port of github.dart additionalSourceAppSpecificSettingFormItems (release options).
    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release options", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "includePrereleases", Label = "Include prereleases", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
            new Forms.OptionFormItem[] { new() { Key = "filterReleaseTitlesByRegEx", Label = "Filter release titles by regular expression", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = true } },
            new Forms.OptionFormItem[] { new() { Key = "invertReleaseTitlesFilter", Label = "Invert regular expression (Filter release titles by regular expression)", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "filterReleaseNotesByRegEx", Label = "Filter release notes by regular expression", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = true } },
            new Forms.OptionFormItem[] { new() { Key = "invertReleaseNotesFilter", Label = "Invert regular expression (Filter release notes by regular expression)", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "verifyLatestTag", Label = "Verify the 'latest' tag", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new()
            {
                Key = "sortMethodChoice", Label = "Sort method", Kind = Forms.OptionFormItemKind.ChoiceList, DefaultString = "date",
                Options = new KeyValuePair<string, string>[]
                {
                    new("date", "Release date"),
                    new("smartname", "Name (smart)"),
                    new("none", "None"),
                    new("smartname-datefallback", "Name (smart) x Release date"),
                    new("name", "Name"),
                }
            } },
            new Forms.OptionFormItem[] { new() { Key = "useLatestAssetDateAsReleaseDate", Label = "Use latest asset upload as release date", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "releaseTitleAsVersion", Label = "Use release title as version string", Kind = Forms.OptionFormItemKind.Switch } },
        };

    public override string Name => "GitHub";
    public override IReadOnlyList<string> Hosts => new[] { "github.com" };

    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl + "/releases";

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/[^/]+/[^/]+");

    public AppNames GetAppNames(string standardUrl)
    {
        var temp = standardUrl.Substring(standardUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        var pathStart = temp.IndexOf('/');
        if (pathStart < 0) throw new InvalidUrlException(Name);
        var names = temp.Substring(pathStart + 1).Split('/');
        if (names.Length == 0 || names[0].Length == 0) throw new InvalidUrlException(Name);
        return new AppNames(names[0], string.Join('/', names.Skip(1)));
    }

    // ------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await FetchReleaseDetailsWithTagFallback(standardUrl, additionalSettings, cancellationToken);
        }
        catch (ObtainiumSourceException) { throw; }
    }

    internal async Task<AssetDetails> FetchReleaseDetailsWithTagFallback(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchReleaseDetails(false, standardUrl, additionalSettings, cancellationToken);
        }
        catch (NoReleasesException) when (GetBool(additionalSettings, "trackOnly"))
        {
            return await FetchReleaseDetails(true, standardUrl, additionalSettings, cancellationToken);
        }
    }

    // ------------------------------------------------------------------
    // HTTP helpers
    // ------------------------------------------------------------------

    private string? GetTokenIfAny(IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var perApp = GetString(additionalSettings, "github-creds");
        return !string.IsNullOrEmpty(perApp) ? perApp : _config.PersonalAccessToken;
    }

    private HttpRequestMessage BuildRequest(string url, IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        var token = GetBool(additionalSettings, "skipAuth") ? null : GetTokenIfAny(additionalSettings);
        if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("Authorization", "Token " + token);
        return req;
    }

    /// Sends the request; on 401/403 with a token configured, retries once
    /// without the token (the token may be invalid for this repository, #3211).
    private async Task<HttpResponseMessage> SourceRequestWithAuthFallback(
        string url, IReadOnlyDictionary<string, JsonElement> additionalSettings, CancellationToken ct)
    {
        var res = await _http.SendAsync(BuildRequest(url, additionalSettings), ct);
        if ((res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            && GetTokenIfAny(additionalSettings) != null && !GetBool(additionalSettings, "skipAuth"))
        {
            var stripped = new Dictionary<string, JsonElement>(additionalSettings)
            {
                ["skipAuth"] = JsonSerializer.SerializeToElement(true),
            };
            res = await _http.SendAsync(BuildRequest(url, stripped), ct);
        }
        return res;
    }

    private string UndoGhProxyMod(string reqUrl)
    {
        var prefix = _config.GhReqPrefix;
        if (string.IsNullOrEmpty(prefix)) return reqUrl;
        var p = "https://" + prefix + "/";
        return reqUrl.StartsWith(p, StringComparison.Ordinal) ? reqUrl.Substring(p.Length) : reqUrl;
    }

    private string ConvertStandardUrlToApiUrl(string standardUrl) =>
        _config.ApiHost + "/repos" + new Uri(standardUrl).AbsolutePath;

    // lib/app_sources/github.dart search + searchCommon
    public override bool CanSearch => true;

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query, IReadOnlyDictionary<string, JsonElement> querySettings, CancellationToken ct = default)
    {
        var url = _config.ApiHost + "/search/repositories?q=" + Uri.EscapeDataString(query) + "&per_page=100";
        var res = await SourceRequestWithAuthFallback(url, new Dictionary<string, JsonElement>(), ct);
        res.EnsureSuccessStatusCode();
        var results = new Dictionary<string, IReadOnlyList<string>>();
        var items = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))?["items"]?.AsArray();
        if (items == null) return results;
        var minStarCount = GetQueryInt(querySettings, "minStarCount");
        foreach (var e in items)
        {
            var stars = e?["stargazers_count"]?.GetValue<int>() ?? e?["stars_count"]?.GetValue<int>() ?? 0;
            if (stars < minStarCount) continue;
            var htmlUrl = e?["html_url"]?.GetValue<string>();
            var fullName = e?["full_name"]?.GetValue<string>();
            if (htmlUrl == null || fullName == null) continue;
            var archived = e?["archived"]?.GetValue<bool>() == true;
            var desc = e?["description"]?.GetValue<string>() ?? "No description";
            results[htmlUrl] = new List<string> { fullName, (archived ? "[ARCHIVED] " : "") + desc };
        }
        return results;
    }

    // ------------------------------------------------------------------
    // Repo rename / archive state
    // ------------------------------------------------------------------

    private async Task<JsonNode?> CheckForRepositoryRename(
        string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings, CancellationToken ct)
    {
        if (!_config.CheckRepoRename) return null;
        var host = new Uri(standardUrl).Host.ToLowerInvariant();
        if (host != Hosts[0] && host != "www." + Hosts[0]) return null;
        var apiUrl = ConvertStandardUrlToApiUrl(standardUrl);
        using var noRedirect = ObtainiumHttp.CreateClient(false);
        noRedirect.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        var req = BuildRequest(apiUrl, additionalSettings);
        var res = await noRedirect.SendAsync(req, ct);
        var code = (int)res.StatusCode;
        if (code >= 300 && code < 400)
        {
            var location = res.Headers.Location?.ToString();
            if (location != null)
            {
                var res2 = await noRedirect.SendAsync(BuildRequest(location, additionalSettings), ct);
                string? newUrl = null;
                try { newUrl = JsonNode.Parse(await res2.Content.ReadAsStringAsync(ct))?["html_url"]?.GetValue<string>(); }
                catch { /* keep null */ }
                if (newUrl != null) throw new RepositoryRenamedException(standardUrl, newUrl);
            }
            return null;
        }
        return DecodeRepoInfo(res);
    }

    private static JsonNode? DecodeRepoInfo(HttpResponseMessage res)
    {
        if ((int)res.StatusCode != 200) return null;
        try { return JsonNode.Parse(res.Content.ReadAsStringAsync().Result); }
        catch { return null; }
    }

    private async Task<JsonNode?> FetchRepoInfoQuietly(
        string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings, CancellationToken ct)
    {
        try
        {
            var res = await SourceRequestWithAuthFallback(ConvertStandardUrlToApiUrl(standardUrl), additionalSettings, ct);
            if ((int)res.StatusCode != 200) return null;
            return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // Release date helpers
    // ------------------------------------------------------------------

    private static DateTimeOffset? GetPublishDateFromRelease(JsonNode? rel)
    {
        var pub = rel?["published_at"]?.GetValue<string>();
        if (pub != null && DateTimeOffset.TryParse(pub, out var dto)) return dto;
        var commitCreated = rel?["commit"]?["created"]?.GetValue<string>();
        if (commitCreated != null && DateTimeOffset.TryParse(commitCreated, out var dto2)) return dto2;
        return null;
    }

    private static DateTimeOffset? NewestAssetDateFromRelease(JsonNode? release)
    {
        var allAssets = release?["assets"] as JsonArray;
        var filteredAssets = release?["filteredAssets"] as JsonArray;
        var assets = filteredAssets is { Count: > 0 } ? filteredAssets : allAssets;
        if (assets == null) return null;
        DateTimeOffset? newest = null;
        foreach (var asset in assets)
        {
            var updated = asset?["updated_at"]?.GetValue<string>() ?? asset?["created_at"]?.GetValue<string>();
            if (updated != null && DateTimeOffset.TryParse(updated, out var dto))
                if (newest == null || dto > newest) newest = dto;
        }
        return newest;
    }

    private static DateTimeOffset? GetReleaseDateFromRelease(JsonNode? rel, bool useAssetDate) =>
        !useAssetDate ? GetPublishDateFromRelease(rel) : NewestAssetDateFromRelease(rel);

    // ------------------------------------------------------------------
    // Sorting
    // ------------------------------------------------------------------

    private static string? ReleaseName(JsonNode? rel) =>
        rel?["tag_name"]?.GetValue<string>() ?? rel?["name"]?.GetValue<string>();

    private void SortGitHubReleases(List<JsonNode?> releases, string sortMethod, bool useLatestAssetDateAsReleaseDate)
    {
        if (sortMethod == "none") return;
        var isDateOnly = sortMethod == "date";
        var dates = new Dictionary<JsonNode, DateTimeOffset?>();
        var formats = new Dictionary<JsonNode, HashSet<string>>();
        if (!isDateOnly)
        {
            foreach (var r in releases)
            {
                if (r == null) continue;
                var name = ReleaseName(r) ?? "";
                formats[r] = VersionService.FindStandardFormatsForVersion(name, strict: false);
            }
        }
        DateTimeOffset? DateOf(JsonNode r)
        {
            if (!dates.TryGetValue(r, out var d))
            {
                d = GetReleaseDateFromRelease(r, useLatestAssetDateAsReleaseDate);
                dates[r] = d;
            }
            return d;
        }
        releases.Sort((a, b) =>
        {
            if (a == null) return -1;
            if (b == null) return 1;
            if (isDateOnly)
                return (DateOf(a) ?? DateTimeOffset.MinValue).CompareTo(DateOf(b) ?? DateTimeOffset.MinValue);
            var nameA = ReleaseName(a) ?? "";
            var nameB = ReleaseName(b) ?? "";
            var stdFormats = new HashSet<string>(formats[a]);
            stdFormats.IntersectWith(formats[b]);
            if (sortMethod == "smartname-datefallback" && stdFormats.Count == 0)
                return (DateOf(a) ?? DateTimeOffset.MinValue).CompareTo(DateOf(b) ?? DateTimeOffset.MinValue);
            if (sortMethod != "name" && stdFormats.Count > 0)
            {
                var sortedFormats = stdFormats.OrderByDescending(x => x.Length).ToList();
                var reg = new Regex(sortedFormats[0]);
                var matchA = reg.Match(nameA);
                var matchB = reg.Match(nameB);
                if (!matchA.Success || !matchB.Success)
                    return StringCompare.CompareAlphaNumeric(nameA, nameB);
                return StringCompare.CompareAlphaNumeric(matchA.Value, matchB.Value);
            }
            return StringCompare.CompareAlphaNumeric(nameA, nameB);
        });
    }

    private static void PositionLatestRelease(List<JsonNode?> releases, JsonNode? latestRelease)
    {
        var latestName = ReleaseName(latestRelease);
        if (latestName == null || releases.Count == 0 || latestName == ReleaseName(releases[releases.Count - 1])) return;
        var ind = releases.FindIndex(e => ReleaseName(e) == latestName);
        if (ind >= 0)
        {
            var item = releases[ind];
            releases.RemoveAt(ind);
            releases.Add(item);
        }
    }

    // ------------------------------------------------------------------
    // Asset extraction
    // ------------------------------------------------------------------

    private List<JsonNode?> FindReleaseAssetUrls(
        JsonNode release, bool includeZips, bool includeTarballs)
    {
        var assets = release["assets"] as JsonArray;
        if (assets == null) return new List<JsonNode?>();
        foreach (var e in assets)
        {
            if (e == null) continue;
            var name = e["name"]?.GetValue<string>() ?? "";
            var url = !AssetFilterService.IsAppOrContainerFile(name, includeZips, includeTarballs)
                ? (e["browser_download_url"]?.GetValue<string>() ?? e["url"]?.GetValue<string>())
                : (e["url"]?.GetValue<string>() ?? e["browser_download_url"]?.GetValue<string>());
            if (url != null) url = UndoGhProxyMod(url);
            var pair = name.Length > 0 && url != null
                ? new JsonArray(name, url)
                : new JsonArray("", "");
            e["final_url"] = pair;
        }
        return assets.ToList();
    }

    private static KeyValuePair<string, string> PairFrom(JsonNode? node)
    {
        if (node is JsonArray arr && arr.Count >= 2)
            return new KeyValuePair<string, string>(arr[0]?.GetValue<string>() ?? "", arr[1]?.GetValue<string>() ?? "");
        return new KeyValuePair<string, string>("", "");
    }

    // ------------------------------------------------------------------
    // Target release selection
    // ------------------------------------------------------------------

    private JsonNode? SelectGitHubTargetRelease(
        List<JsonNode?> releases,
        bool fallbackToOlderReleases,
        bool includePrereleases,
        string? regexFilter,
        string? regexNotesFilter,
        bool includeZips,
        bool includeTarballs,
        bool useLatestAssetDateAsReleaseDate,
        int minAgeDays,
        IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var releaseSkipped = 0;
        var userFilteredOut = 0;
        var titleRegex = regexFilter != null ? new Regex(regexFilter) : null;
        var notesRegex = regexNotesFilter != null ? new Regex(regexNotesFilter) : null;
        var invertTitleFilter = GetBool(additionalSettings, "invertReleaseTitlesFilter");
        var invertNotesFilter = GetBool(additionalSettings, "invertReleaseNotesFilter");
        for (var i = 0; i < releases.Count; i++)
        {
            if (!fallbackToOlderReleases && i > releaseSkipped) break;
            var rel = releases[i];
            if (rel == null) continue;
            if (!includePrereleases && rel["prerelease"]?.GetValue<bool>() == true) { releaseSkipped++; continue; }
            if (rel["draft"]?.GetValue<bool>() == true) { releaseSkipped++; continue; }
            var nameToFilter = rel["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(nameToFilter)) nameToFilter = rel["tag_name"]?.GetValue<string>() ?? "";
            if (titleRegex != null && !PassesRegExFilter(titleRegex, nameToFilter.Trim(), invertTitleFilter))
            { userFilteredOut++; continue; }
            if (notesRegex != null && !PassesRegExFilter(notesRegex, (rel["body"]?.GetValue<string>() ?? "").Trim(), invertNotesFilter))
            { userFilteredOut++; continue; }
            if (IsReleaseTooYoung(GetReleaseDateFromRelease(rel, useLatestAssetDateAsReleaseDate), minAgeDays))
            { releaseSkipped++; continue; }

            // Assets are JSON objects; their [name, url] pair lives under
            // "final_url" (upstream: e['final_url'] as MapEntry).
            KeyValuePair<string, string> PairOf(JsonNode? asset) => PairFrom(asset?["final_url"]);
            var allAssetsWithUrls = FindReleaseAssetUrls(rel, includeZips, includeTarballs);
            var allAssetUrls = allAssetsWithUrls.Select(PairOf).ToList();
            var appAssetsWithUrls = allAssetsWithUrls.Where(e =>
                AssetFilterService.IsAppOrContainerFile(PairOf(e).Key, includeZips, includeTarballs)).ToList();
            var filteredAssetPairs = AssetFilterService.FilterAssets(
                appAssetsWithUrls.Select(PairOf).ToList(),
                GetString(additionalSettings, "apkFilterRegEx"),
                GetBool(additionalSettings, "invertAPKFilter") ? true : (bool?)null);
            var filteredAssets = appAssetsWithUrls
                .Where(e => filteredAssetPairs.Any(e2 => e2.Key == PairOf(e).Key))
                .ToList();

            if (filteredAssetPairs.Count == 0 && !GetBool(additionalSettings, "trackOnly"))
            { userFilteredOut++; continue; }

            var targetRelease = rel;
            var filteredPairsJson = new JsonArray();
            foreach (var p in filteredAssetPairs) filteredPairsJson.Add(new JsonArray(p.Key, p.Value));
            targetRelease["apkUrls"] = filteredPairsJson;
            var filteredAssetsJson = new JsonArray();
            foreach (var a in filteredAssets) filteredAssetsJson.Add(a?.DeepClone());
            targetRelease["filteredAssets"] = filteredAssetsJson;
            targetRelease["version"] = GetBool(additionalSettings, "releaseTitleAsVersion")
                ? nameToFilter
                : (rel["tag_name"]?.GetValue<string>() ?? rel["name"]?.GetValue<string>());
            var version = targetRelease["version"]?.GetValue<string>();
            var tarballUrl = rel["tarball_url"]?.GetValue<string>();
            if (tarballUrl != null)
                allAssetUrls.Add(new KeyValuePair<string, string>((version ?? "source") + ".tar.gz", UndoGhProxyMod(tarballUrl)));
            var zipballUrl = rel["zipball_url"]?.GetValue<string>();
            if (zipballUrl != null)
                allAssetUrls.Add(new KeyValuePair<string, string>((version ?? "source") + ".zip", UndoGhProxyMod(zipballUrl)));
            var allPairsJson = new JsonArray();
            foreach (var p in allAssetUrls) allPairsJson.Add(new JsonArray(p.Key, p.Value));
            targetRelease["allAssetUrls"] = allPairsJson;
            targetRelease["newerReleasesFilteredOut"] = userFilteredOut;
            return targetRelease;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Main fetch
    // ------------------------------------------------------------------

    private async Task<AssetDetails> FetchReleaseDetails(
        bool useTagUrl,
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken ct)
    {
        JsonNode? repoInfo = await CheckForRepositoryRename(standardUrl, additionalSettings, ct);
        if (repoInfo == null && _config.UpdateHealthArchivedEnabled)
            repoInfo = await FetchRepoInfoQuietly(standardUrl, additionalSettings, ct);
        bool? isArchived = repoInfo == null
            ? null
            : (repoInfo["archived"]?.GetValue<bool>() == true || repoInfo["disabled"]?.GetValue<bool>() == true);

        var includePrereleases = GetBool(additionalSettings, "includePrereleases");
        var fallbackToOlderReleases = GetBool(additionalSettings, "fallbackToOlderReleases");
        var regexFilter = GetString(additionalSettings, "filterReleaseTitlesByRegEx");
        if (string.IsNullOrEmpty(regexFilter)) regexFilter = null;
        var regexNotesFilter = GetString(additionalSettings, "filterReleaseNotesByRegEx");
        if (string.IsNullOrEmpty(regexNotesFilter)) regexNotesFilter = null;
        var verifyLatestTag = GetBool(additionalSettings, "verifyLatestTag");
        var useLatestAssetDateAsReleaseDate = GetBool(additionalSettings, "useLatestAssetDateAsReleaseDate");
        var sortMethod = GetString(additionalSettings, "sortMethodChoice") ?? "smartname-datefallback";
        var includeZips = GetBool(additionalSettings, "includeZips");
        var includeTarballs = GetBool(additionalSettings, "includeTarballs");
        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") ?? _config.GlobalMinimumUpdateAgeDays;

        var requestUrl = ConvertStandardUrlToApiUrl(standardUrl) + (useTagUrl ? "/tags" : "/releases") + "?per_page=100";

        JsonNode? latestRelease = null;
        if (verifyLatestTag && !useTagUrl)
        {
            var latestUrl = requestUrl.Replace("/releases?per_page=100", "/releases/latest");
            var resL = await SourceRequestWithAuthFallback(latestUrl, additionalSettings, ct);
            if ((int)resL.StatusCode != 200) throw new ObtainiumHttpException((int)resL.StatusCode, latestUrl);
            latestRelease = JsonNode.Parse(await resL.Content.ReadAsStringAsync(ct));
        }

        var res = await SourceRequestWithAuthFallback(requestUrl, additionalSettings, ct);
        if ((int)res.StatusCode != 200) throw new ObtainiumHttpException((int)res.StatusCode, requestUrl);

        var decoded = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        if (decoded is not JsonArray decodedArray) throw new NoReleasesException();
        var releases = decodedArray.ToList();

        if (latestRelease != null)
        {
            var latestTag = ReleaseName(latestRelease);
            if (releases.All(e => ReleaseName(e) != latestTag))
                releases.Insert(0, latestRelease);
        }

        if (sortMethod == "none")
        {
            releases.Reverse();
        }
        else
        {
            SortGitHubReleases(releases, sortMethod, useLatestAssetDateAsReleaseDate);
        }
        PositionLatestRelease(releases, latestRelease);
        releases.Reverse();

        JsonNode? targetRelease = null;
        foreach (var age in minAgeDays > 0 ? new[] { minAgeDays, 0 } : new[] { 0 })
        {
            targetRelease = SelectGitHubTargetRelease(
                releases, fallbackToOlderReleases, includePrereleases,
                regexFilter, regexNotesFilter, includeZips, includeTarballs,
                useLatestAssetDateAsReleaseDate, age, additionalSettings);
            if (targetRelease != null) break;
        }
        if (targetRelease == null) throw new NoReleasesException();

        var version = targetRelease["version"]?.GetValue<string>();
        var releaseDate = GetReleaseDateFromRelease(targetRelease, useLatestAssetDateAsReleaseDate);
        if (string.IsNullOrEmpty(version)) throw new NoVersionException();

        var assetUrls = (targetRelease["apkUrls"] as JsonArray ?? new JsonArray()).Select(PairFrom).ToList();
        var allAssetUrls = (targetRelease["allAssetUrls"] as JsonArray ?? new JsonArray()).Select(PairFrom).ToList();
        var changeLog = (targetRelease["body"]?.GetValue<string>() ?? "");
        var newerFiltered = targetRelease["newerReleasesFilteredOut"]?.GetValue<int>();

        return new AssetDetails(
            version,
            assetUrls,
            GetAppNames(standardUrl),
            releaseDate,
            changeLog.Length == 0 ? null : changeLog,
            allAssetUrls,
            isArchived,
            newerFiltered);
    }
}
