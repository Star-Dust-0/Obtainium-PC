using System.Net.Http;
using System.Text.Json;

namespace ObtainiumPC.Sources.VivoAppStore;

/// <summary>
/// Port of lib/app_sources/vivoappstore.dart: Vivo's h5 detail API.
/// Track-only on Windows. Vivo's endpoints redirect to plain-HTTP CDN URLs,
/// so the client follows redirects across schemes.
/// </summary>
public sealed class VivoAppStoreSource : AppSourceBase
{
    private const string AppDetailUrl = "https://detail-browser.vivo.com.cn/v115/index.html?appId=";
    private const string AppDetailJsonUrl = "https://h5-api.appstore.vivo.com.cn/detailInfo?appId=";
    private const string ApkDownloadUrl = "https://appstore.vivo.com.cn/appinfo/downloadApkFile?id=";

    private readonly HttpClient _http;

    public VivoAppStoreSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Vivo App Store";
    public override IReadOnlyList<string> Hosts => new[]
    {
        "h5.appstore.vivo.com.cn",
        "h5coml.vivo.com.cn",
        "detail-browser.vivo.com.cn",
    };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;

    private static string ParseVivoAppId(string url)
    {
        var query = new Uri(url.Replace("/#", "")).Query;
        var appId = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "appId")
            .Select(p => Uri.UnescapeDataString(p[1]))
            .FirstOrDefault();
        if (string.IsNullOrEmpty(appId)) throw new InvalidUrlException("Vivo App Store");
        return appId;
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        AppDetailUrl + ParseVivoAppId(url);

    private async Task<JsonElement> GetDetailJson(string standardUrl, CancellationToken ct)
    {
        var vivoAppId = ParseVivoAppId(standardUrl);
        var detailUrl = AppDetailJsonUrl + Uri.EscapeDataString(vivoAppId);
        var res = await _http.GetAsync(detailUrl, ct);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, detailUrl);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("id", out _))
        {
            doc.Dispose();
            throw new NoReleasesException();
        }
        return doc.RootElement.Clone();
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var json = await GetDetailJson(standardUrl, cancellationToken);
        var versionName = json.TryGetProperty("version_name", out var v) ? v.ToString() : null;
        var id = json.TryGetProperty("id", out var i) ? i.ToString() : null;
        if (string.IsNullOrEmpty(versionName)) throw new FDroid.NoVersionException();
        if (string.IsNullOrEmpty(id)) throw new NoAppAssetException();

        var apkUrl = ApkDownloadUrl + Uri.EscapeDataString(id);
        var appName = json.TryGetProperty("title_zh", out var t) ? t.ToString() : "App";
        var packageName = json.TryGetProperty("package_name", out var p) ? p.ToString() : "";
        var versionCode = json.TryGetProperty("version_code", out var vc) ? vc.ToString() : "";
        var developer = json.TryGetProperty("developer", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : Name;
        DateTimeOffset? releaseDate = null;
        if (json.TryGetProperty("upload_time", out var ut) && DateTimeOffset.TryParse(ut.ToString(), out var parsed))
            releaseDate = parsed;

        return new AssetDetails(
            versionName,
            new List<KeyValuePair<string, string>> { new($"{packageName}_{versionCode}.apk", apkUrl) },
            new AppNames(developer, appName),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
