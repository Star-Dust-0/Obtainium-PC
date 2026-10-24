using System.Net.Http;
using System.Text.Json;

namespace ObtainiumPC.Sources.ApkPure;

/// <summary>
/// Port of lib/app_sources/apkpure.dart: APKPure store via the tapi.pureapk.com
/// version-history API. Windows mappings: the device-info header carries a
/// fixed os_ver "30" (no Android SDK level exists on Windows), and the ABI
/// list is empty, so with autoApkFilterByArch only universal/unlimited
/// variants survive (upstream filters against the phone's real ABIs).
/// Track-only on Windows.
/// </summary>
public sealed class ApkPureSource : AppSourceBase
{
    private const string ApiBaseUrl = "https://tapi.pureapk.com/v3/get_app_his_version?package_name";

    private readonly HttpClient _http;

    public ApkPureSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public override string Name => "APKPure";
    public override IReadOnlyList<string> Hosts => new[] { "apkpure.net", "apkpure.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release selection", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
            new Forms.OptionFormItem[] { new() { Key = "stayOneVersionBehind", Label = "Stay one version behind", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "useFirstApkOfVersion", Label = "Use first APK of version", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
        };

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var hostsPattern = @"(apkpure\.net|apkpure\.com)";
        var matchB = System.Text.RegularExpressions.Regex.Match(
            url, $"^https?://m\\.{hostsPattern}(/+[^/]{{2}})?/+[^/]+/+[^/]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (matchB.Success && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.StartsWith("m."))
            url = $"https://{uri.Host.Substring(2)}{uri.AbsolutePath}";
        var matchA = System.Text.RegularExpressions.Regex.Match(
            url, $"^https?://(www\\.)?{hostsPattern}(/+[^/]{{2}})?/+[^/]+/+[^/]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return matchA.Success ? matchA.Value : throw new InvalidUrlException(Name);
    }

    private static HttpRequestMessage BuildRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Windows mapping: no Android SDK level exists; fixed os_ver.
        req.Headers.TryAddWithoutValidation("Ual-Access-Businessid", "projecta");
        req.Headers.TryAddWithoutValidation("Ual-Access-ProjectA", """{"device_info":{"os_ver":"30"}}""");
        return req;
    }

    private static AssetDetails GetDetailsForVersion(
        List<JsonElement> versionVariants,
        IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var apkUrls = new List<KeyValuePair<string, string>>();
        foreach (var e in versionVariants)
        {
            var appId = e.TryGetProperty("package_name", out var pn) ? pn.ToString() : null;
            var versionCode = e.TryGetProperty("version_code", out var vc) ? vc.ToString() : null;
            if (appId == null || versionCode == null) continue;

            var architectures = new List<string>();
            if (e.TryGetProperty("native_code", out var nc) && nc.ValueKind == JsonValueKind.Array)
                architectures = nc.EnumerateArray().Select(a => a.ToString()).ToList();
            var architectureString = string.Join(',', architectures);
            if (architectures.Contains("universal") || architectures.Contains("unlimited"))
                architectures.Clear();
            // Windows mapping: the supported-ABI list is empty (no Android device).
            if (GetBool(additionalSettings, "autoApkFilterByArch") && architectures.Count > 0)
                continue;

            string? type = null, downloadUri = null;
            if (e.TryGetProperty("asset", out var asset) && asset.ValueKind == JsonValueKind.Object)
            {
                type = asset.TryGetProperty("type", out var t) ? t.ToString() : null;
                downloadUri = asset.TryGetProperty("url", out var u) ? u.ToString() : null;
            }
            if (type == null || downloadUri == null) continue;

            var archSuffix = architectureString.Length > 0 ? $"-{architectureString}" : "";
            apkUrls.Add(new KeyValuePair<string, string>(
                $"{appId}-{versionCode}{archSuffix}.{type.ToLowerInvariant()}", downloadUri));
        }
        var seenKeys = new HashSet<string>();
        apkUrls = apkUrls.Where(p => seenKeys.Add(p.Key)).ToList();
        if (apkUrls.Count == 0) throw new NoAppAssetException();

        var v = versionVariants[0];
        var version = v.TryGetProperty("version_name", out var vn) ? vn.ToString() : null;
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
        var author = v.TryGetProperty("developer", out var dev) ? dev.ToString() : "APKPure";
        var appName = v.TryGetProperty("title", out var title) ? title.ToString() : "App";
        DateTimeOffset? releaseDate = null;
        if (v.TryGetProperty("update_date", out var ud)
            && DateTimeOffset.TryParse(ud.ToString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            releaseDate = parsed;
        string? changeLog = v.TryGetProperty("whatsnew", out var wn) ? wn.ToString() : null;
        if (string.IsNullOrEmpty(changeLog)) changeLog = null;

        if (GetBool(additionalSettings, "useFirstApkOfVersion"))
            apkUrls = new List<KeyValuePair<string, string>> { apkUrls[0] };

        return new AssetDetails(
            version,
            apkUrls,
            new AppNames(author, appName),
            releaseDate,
            changeLog,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        var res = await _http.SendAsync(BuildRequest($"{ApiBaseUrl}={appId}&hl=en"), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);

        List<JsonElement> apks;
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            apks = doc.RootElement.GetProperty("version_list").EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch { throw new NoReleasesException(); }

        // Group variants by version_name, preserving first-seen order (Dart's
        // LinkedHashMap fold).
        var groupOrder = new List<string>();
        var groups = new Dictionary<string, List<JsonElement>>();
        foreach (var e in apks)
        {
            var v = e.TryGetProperty("version_name", out var vn) ? vn.ToString() : "";
            if (!groups.TryGetValue(v, out var list))
            {
                list = new List<JsonElement>();
                groups[v] = list;
                groupOrder.Add(v);
            }
            list.Add(e);
        }
        var versions = groupOrder.Select(k => groups[k]).ToList();
        if (versions.Count == 0) throw new NoReleasesException();

        var minAgeDays = GetInt(additionalSettings, "minimumUpdateAgeDays") ?? 0;
        DateTimeOffset? VersionUpdateDate(List<JsonElement> variants)
        {
            if (variants[0].TryGetProperty("update_date", out var raw)
                && DateTimeOffset.TryParse(raw.ToString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
                return d;
            return null;
        }

        List<JsonElement>? tooYoungVersion = null;
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            try
            {
                if (i == 0 && GetBool(additionalSettings, "stayOneVersionBehind"))
                {
                    if (!GetBool(additionalSettings, "fallbackToOlderReleases") && versions.Count < 2)
                        throw new NoReleasesException();
                    continue;
                }
                var date = VersionUpdateDate(v);
                if (minAgeDays > 0 && date is { } d && DateTimeOffset.Now - d < TimeSpan.FromDays(minAgeDays))
                {
                    tooYoungVersion ??= v;
                    continue;
                }
                return GetDetailsForVersion(v, additionalSettings);
            }
            catch (Exception)
            {
                if (!GetBool(additionalSettings, "fallbackToOlderReleases") || i == versions.Count - 1)
                    throw;
            }
        }
        // No version old enough: use the newest so the caller can suppress it.
        if (tooYoungVersion != null)
            return GetDetailsForVersion(tooYoungVersion, additionalSettings);
        throw new NoAppAssetException();
    }
}
