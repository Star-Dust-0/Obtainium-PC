using System.Net.Http;
using System.Text.Json;

namespace ObtainiumPC.Sources.Tencent;

/// <summary>
/// Port of lib/app_sources/tencent.dart: Tencent MyApp store. App details are
/// embedded as a JS variable in the simple.jsp response. Track-only on Windows.
/// </summary>
public sealed class TencentSource : AppSourceBase
{
    private const string JsVar = "window.systemData=";

    private readonly HttpClient _http;

    public TencentSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Tencent App Store";
    public override IReadOnlyList<string> Hosts => new[] { "sj.qq.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: "", pathPattern: @"/appdetail/[^/]+");

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var hostParts = new Uri(standardUrl).Host.Split('.');
        var baseHost = string.Join('.', hostParts[^2], hostParts[^1]);

        var res = await _http.GetAsync($"https://a.app.{baseHost}/o/simple.jsp?pkgname={appId}", cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);

        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        var jsonLine = body.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(JsVar));
        if (jsonLine == null) throw new NoReleasesException();

        using var doc = JsonDocument.Parse(jsonLine[JsVar.Length..]);
        if (!doc.RootElement.TryGetProperty("appDetail", out var json) || json.ValueKind != JsonValueKind.Object)
            throw new NoReleasesException();

        var version = json.TryGetProperty("versionName", out var v) ? v.ToString() : null;
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();

        var apkUrl = json.TryGetProperty("apkUrl64", out var u64) && u64.ValueKind == JsonValueKind.String ? u64.GetString() : null;
        apkUrl ??= json.TryGetProperty("apkUrl", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        if (apkUrl == null) throw new NoAppAssetException();

        var appName = json.TryGetProperty("appName", out var an) ? an.ToString() : "App";
        var author = json.TryGetProperty("author", out var au) ? au.ToString() : Name;

        var fsname = new Uri(apkUrl).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "fsname")
            .Select(p => Uri.UnescapeDataString(p[1]))
            .FirstOrDefault();
        var apkName = fsname ?? $"{appId}_{version}.apk";

        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new(apkName, apkUrl) },
            new AppNames(author, appName),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
