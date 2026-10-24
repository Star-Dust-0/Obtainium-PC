using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace ObtainiumPC.Sources.FDroid;

/// <summary>
/// Port of lib/app_sources/fdroidrepo.dart: third-party F-Droid repositories.
/// Prefers index-v2.json, falls back to the legacy index.xml. Track-only on
/// Windows (Android packages); ABI nativecode filtering is not ported.
/// This source has no fixed hosts - it is selected via the override dropdown.
/// </summary>
public sealed class FDroidRepoSource : AppSourceBase
{
    private sealed record FdroidVersion(
        string VersionName,
        int VersionCode,
        string ApkName,
        DateTimeOffset? Added,
        IReadOnlyList<string> ReleaseChannels);

    private sealed record FdroidIndexEntry(
        string Id,
        string Name,
        string Summary,
        string? Author,
        string? Changelog,
        int? MarketVersionCode,
        List<FdroidVersion> Versions);

    private sealed record FdroidIndex(string BaseUrl, List<FdroidIndexEntry> Entries);

    private readonly HttpClient _http;

    public FDroidRepoSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "F-Droid Third-Party Repo";
    public override IReadOnlyList<string> Hosts => Array.Empty<string>();
    public override bool SupportsInstall => false;

    private static string RemoveQueryParamsFromUrl(string url, params string[] keep)
    {
        var uri = new Uri(url);
        var kept = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var result = System.Web.HttpUtility.ParseQueryString("");
        foreach (var key in keep)
            if (kept[key] is { } value) result[key] = value;
        var query = result.ToString();
        var builder = new UriBuilder(uri) { Query = query };
        return builder.Uri.ToString().TrimEnd('?');
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var uri = new UriBuilder(url);
        var segments = uri.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && (segments[^1] == "index.xml" || segments[^1] == "index-v2.json"))
        {
            segments.RemoveAt(segments.Count - 1);
            uri.Path = string.Join('/', segments);
        }
        return RemoveQueryParamsFromUrl(uri.Uri.ToString(), "appId");
    }

    private async Task<string?> FetchString(string url, CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync(url, ct);
            return (int)res.StatusCode == 200 ? await res.Content.ReadAsStringAsync(ct) : null;
        }
        catch { return null; }
    }

    private static string StripLastPathSegment(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
        return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}/{string.Join('/', segments)}";
    }

    private async Task<FdroidIndex?> TryFetchIndexV2(string url, CancellationToken ct)
    {
        var base_ = url.EndsWith("/index-v2.json") ? StripLastPathSegment(url) : url.TrimEnd('/');
        var candidates = new[]
        {
            url.EndsWith("/index-v2.json") ? url : url.TrimEnd('/') + "/index-v2.json",
            base_ + "/repo/index-v2.json",
            base_ + "/fdroid/repo/index-v2.json",
        };
        foreach (var candidate in candidates)
        {
            var body = await FetchString(candidate, ct);
            if (body == null) continue;
            var index = ParseIndexV2(body, candidate);
            if (index != null) return index;
        }
        return null;
    }

    private static string? LocalizedString(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return string.IsNullOrEmpty(value.GetString()) ? null : value.GetString();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "en-US", "en" })
                if (value.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                    return v.GetString();
            foreach (var prop in value.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(prop.Value.GetString()))
                    return prop.Value.GetString();
        }
        return null;
    }

    private static DateTimeOffset? ParseV2Timestamp(JsonElement added)
    {
        if (added.ValueKind == JsonValueKind.Number && added.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms > 1_000_000_000_000 ? ms : ms * 1000);
        if (added.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(added.GetString(), out var parsed))
            return parsed;
        return null;
    }

    private static FdroidIndex? ParseIndexV2(string body, string requestUrl)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
                return null;
            var baseUrl = StripLastPathSegment(requestUrl);
            var entries = new List<FdroidIndexEntry>();
            foreach (var pkgProp in packages.EnumerateObject())
            {
                var pkg = pkgProp.Value;
                if (!pkg.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Object)
                    continue;
                var versionList = new List<FdroidVersion>();
                foreach (var vProp in versions.EnumerateObject())
                {
                    var v = vProp.Value;
                    if (!v.TryGetProperty("manifest", out var manifest) || !v.TryGetProperty("file", out var file))
                        continue;
                    var versionName = manifest.TryGetProperty("versionName", out var vn) ? vn.ToString() : null;
                    var versionCode = manifest.TryGetProperty("versionCode", out var vc) && vc.TryGetInt32(out var code) ? code : (int?)null;
                    var apkName = file.TryGetProperty("name", out var an) ? an.GetString() : null;
                    if (versionName == null || versionCode == null || string.IsNullOrEmpty(apkName)) continue;
                    var channels = new List<string>();
                    if (v.TryGetProperty("releaseChannels", out var rc) && rc.ValueKind == JsonValueKind.Array)
                        channels.AddRange(rc.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!));
                    versionList.Add(new FdroidVersion(
                        versionName,
                        versionCode.Value,
                        apkName.TrimStart('/'),
                        v.TryGetProperty("added", out var added) ? ParseV2Timestamp(added) : null,
                        channels));
                }
                if (versionList.Count == 0) continue;
                versionList.Sort((a, b) => b.VersionCode.CompareTo(a.VersionCode));
                string? name = null, summary = null, author = null, changelog = null;
                if (pkg.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                {
                    if (metadata.TryGetProperty("name", out var n)) name = LocalizedString(n);
                    if (metadata.TryGetProperty("summary", out var sm)) summary = LocalizedString(sm);
                    if (metadata.TryGetProperty("authorName", out var au)) author = LocalizedString(au);
                    if (metadata.TryGetProperty("changelog", out var cl)) changelog = LocalizedString(cl);
                }
                entries.Add(new FdroidIndexEntry(pkgProp.Name, name ?? pkgProp.Name, summary ?? "", author, changelog, null, versionList));
            }
            return entries.Count == 0 ? null : new FdroidIndex(baseUrl, entries);
        }
        catch { return null; }
    }

    private async Task<FdroidIndex> FetchIndexV1(string url, CancellationToken ct)
    {
        var candidates = new[]
        {
            url.EndsWith("/index.xml") ? url : url.TrimEnd('/') + "/index.xml",
        };
        var base_ = url.EndsWith("/index.xml") ? StripLastPathSegment(url) : url.TrimEnd('/');
        candidates = candidates.Concat(new[] { base_ + "/repo/index.xml", base_ + "/fdroid/repo/index.xml" }).ToArray();

        string? body = null;
        string? usedUrl = null;
        foreach (var candidate in candidates)
        {
            body = await FetchString(candidate, ct);
            if (body != null) { usedUrl = candidate; break; }
        }
        if (body == null || usedUrl == null) throw new NoReleasesException();

        var doc = XDocument.Parse(body);
        var baseUrl = StripLastPathSegment(usedUrl);
        var entries = new List<FdroidIndexEntry>();
        foreach (var app in doc.Descendants("application"))
        {
            var id = app.Attribute("id")?.Value;
            if (id == null) continue;
            var versions = new List<FdroidVersion>();
            foreach (var pkg in app.Elements("package"))
            {
                var versionName = pkg.Element("version")?.Value;
                var versionCode = int.TryParse(pkg.Element("versioncode")?.Value ?? "", out var code) ? code : (int?)null;
                var apkName = pkg.Element("apkname")?.Value;
                if (versionName == null || versionCode == null || string.IsNullOrEmpty(apkName)) continue;
                var added = pkg.Element("added")?.Value;
                versions.Add(new FdroidVersion(
                    versionName,
                    versionCode.Value,
                    apkName,
                    added != null && DateTimeOffset.TryParse(added, out var parsed) ? parsed : null,
                    Array.Empty<string>()));
            }
            versions.Sort((a, b) => b.VersionCode.CompareTo(a.VersionCode));
            entries.Add(new FdroidIndexEntry(
                id,
                app.Element("name")?.Value ?? id,
                app.Element("summary")?.Value ?? "",
                app.Element("author")?.Value,
                app.Element("changelog")?.Value,
                int.TryParse(app.Element("marketvercode")?.Value ?? "", out var mvc) ? mvc : null,
                versions));
        }
        return new FdroidIndex(baseUrl, entries);
    }

    private async Task<FdroidIndex> FetchIndex(string url, CancellationToken ct) =>
        await TryFetchIndexV2(url, ct) ?? await FetchIndexV1(url, ct);

    private static FdroidIndexEntry? FindIndexEntry(List<FdroidIndexEntry> entries, string appIdOrName)
    {
        var byId = entries.FirstOrDefault(e => e.Id == appIdOrName);
        if (byId != null) return byId;
        var byName = entries.FirstOrDefault(e => e.Name.Equals(appIdOrName, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName;
        return entries.FirstOrDefault(e => e.Name.Contains(appIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        string? appIdOrName = SettingString(additionalSettings, "appIdOrName");
        var appIdParam = System.Web.HttpUtility.ParseQueryString(new Uri(standardUrl).Query)["appId"];
        if (appIdParam != null) appIdOrName = appIdParam;
        standardUrl = RemoveQueryParamsFromUrl(standardUrl);
        if (appIdOrName == null) throw new NoReleasesException();

        var pickHighestVersionCode = SettingBool(additionalSettings, "pickHighestVersionCode");
        var trySelectingSuggestedVersionCode = SettingBool(additionalSettings, "trySelectingSuggestedVersionCode");

        var index = await FetchIndex(standardUrl, cancellationToken);
        var entry = FindIndexEntry(index.Entries, appIdOrName)
            ?? throw new ObtainiumSourceException($"No app with ID or name '{appIdOrName}' found in this repository");
        var releases = entry.Versions;
        if (releases.Count == 0) throw new NoReleasesException();

        var selected = new List<FdroidVersion>();
        if (trySelectingSuggestedVersionCode && entry.MarketVersionCode != null)
            selected = releases.Where(v => v.VersionCode == entry.MarketVersionCode).ToList();

        var candidates = releases;
        if (selected.Count == 0 && trySelectingSuggestedVersionCode)
        {
            // index-v2 has no suggested version code; the toggle instead means
            // "prefer stable" - drop builds marked as non-stable (e.g. Beta).
            var nonStable = releases
                .Where(v => v.ReleaseChannels.Count > 0 && !v.ReleaseChannels.Any(c => c.Equals("stable", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (nonStable.Count > 0 && nonStable.Count < releases.Count)
                candidates = releases.Where(v => !nonStable.Contains(v)).ToList();
        }

        if (selected.Count == 0)
        {
            if (pickHighestVersionCode)
            {
                selected = new List<FdroidVersion> { candidates[0] };
            }
            else
            {
                var latestVersionName = candidates[0].VersionName;
                selected = candidates.Where(v => v.VersionName == latestVersionName).ToList();
            }
        }
        if (selected.Count == 0) throw new NoReleasesException();

        var useVersionCode = SettingBool(additionalSettings, "useVersionCodeAsOSVersion");
        return new AssetDetails(
            useVersionCode ? selected[0].VersionCode.ToString() : selected[0].VersionName,
            selected.Select(v => new KeyValuePair<string, string>(v.ApkName, $"{index.BaseUrl}/{v.ApkName}")).ToList(),
            new AppNames(entry.Author ?? Name, entry.Name),
            selected[0].Added,
            entry.Changelog,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    private static bool SettingBool(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.True;

    private static string? SettingString(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString())
            ? el.GetString() : null;
}
