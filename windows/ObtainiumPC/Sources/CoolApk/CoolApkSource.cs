using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ObtainiumPC.Sources.CoolApk;

/// <summary>
/// Port of lib/app_sources/coolapk.dart: CoolApk store. Uses the v6 API with a
/// client token adapted (as upstream) from FuckCoolapkTokenV2 / Coolapk-UWP.
/// Client version and device fingerprint are locked to a specific client
/// release and may need periodic updates. Track-only on Windows.
/// </summary>
public sealed class CoolApkSource : AppSourceBase
{
    private const string ApiBaseUrl = "https://api2.coolapk.com";
    private const string CoolApkUserAgent =
        "Dalvik/2.1.0 (Linux; U; Android 9; MI 8 SE MIUI/9.5.9) (#Build; Xiaomi; MI 8 SE; PKQ1.181121.001; 9) +CoolMarket/12.4.2-2208241-universal";

    private readonly HttpClient _http;
    private readonly HttpClient _noRedirect;

    public CoolApkSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        // The download endpoint answers with a redirect to the real APK URL;
        // follow it manually. When a client is injected (tests) it serves both.
        _noRedirect = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    public override string Name => "CoolApk";
    public override IReadOnlyList<string> Hosts => new[] { "coolapk.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/apk/[^/]+");

    private static HttpRequestMessage BuildRequest(string url)
    {
        var (deviceCode, token) = GetToken();
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", CoolApkUserAgent);
        req.Headers.TryAddWithoutValidation("X-App-Id", "com.coolapk.market");
        req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("X-Sdk-Int", "30");
        req.Headers.TryAddWithoutValidation("X-App-Mode", "universal");
        req.Headers.TryAddWithoutValidation("X-App-Channel", "coolapk");
        req.Headers.TryAddWithoutValidation("X-Sdk-Locale", "zh-CN");
        req.Headers.TryAddWithoutValidation("X-App-Version", "12.4.2");
        req.Headers.TryAddWithoutValidation("X-Api-Supported", "2208241");
        req.Headers.TryAddWithoutValidation("X-App-Code", "2208241");
        req.Headers.TryAddWithoutValidation("X-Api-Version", "12");
        req.Headers.TryAddWithoutValidation("X-App-Device", deviceCode);
        req.Headers.TryAddWithoutValidation("X-Dark-Mode", "0");
        req.Headers.TryAddWithoutValidation("X-App-Token", token);
        return req;
    }

    // The same signed headers accompany the APK download (upstream
    // getRequestHeaders with forAPKDownload).
    public override IReadOnlyDictionary<string, string>? GetDownloadRequestHeaders(
        string assetUrl, string standardUrl, IReadOnlyDictionary<string, JsonElement> additionalSettings)
    {
        var (deviceCode, token) = GetToken();
        return new Dictionary<string, string>
        {
            ["User-Agent"] = CoolApkUserAgent,
            ["X-App-Id"] = "com.coolapk.market",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["X-Sdk-Int"] = "30",
            ["X-App-Mode"] = "universal",
            ["X-App-Channel"] = "coolapk",
            ["X-Sdk-Locale"] = "zh-CN",
            ["X-App-Version"] = "12.4.2",
            ["X-Api-Supported"] = "2208241",
            ["X-App-Code"] = "2208241",
            ["X-Api-Version"] = "12",
            ["X-App-Device"] = deviceCode,
            ["X-Dark-Mode"] = "0",
            ["X-App-Token"] = token,
        };
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        var res = await _http.SendAsync(BuildRequest($"{ApiBaseUrl}/v6/apk/detail?id={appId}"), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken)); }
        catch { throw new NoReleasesException(); }
        using (doc)
        {
            var root = doc.RootElement;
            if ((root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() == -2)
                || !root.TryGetProperty("data", out var detail) || detail.ValueKind != JsonValueKind.Object)
                throw new NoReleasesException();

            var version = detail.TryGetProperty("apkversionname", out var v) ? v.ToString() : "";
            if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
            var appName = detail.TryGetProperty("title", out var t) ? t.ToString() : "App";
            var author = detail.TryGetProperty("developername", out var d) ? d.ToString() : Name;
            var changeLog = detail.TryGetProperty("changelog", out var c) ? c.ToString() : "";

            DateTimeOffset? releaseDate = null;
            if (detail.TryGetProperty("lastupdate", out var lu))
            {
                long? seconds = lu.ValueKind == JsonValueKind.Number && lu.TryGetInt64(out var l) ? l
                    : long.TryParse(lu.ToString(), out var p) ? p : null;
                if (seconds is { } s) releaseDate = DateTimeOffset.FromUnixTimeSeconds(s);
            }

            var aid = detail.TryGetProperty("id", out var id) ? id.ToString() : "";
            if (aid.Length == 0) throw new NoReleasesException();

            // The download endpoint redirects to the real APK URL.
            var dlRes = await _noRedirect.SendAsync(
                BuildRequest($"{ApiBaseUrl}/v6/apk/download?pn={appId}&aid={aid}"), cancellationToken);
            var apkUrl = (int)dlRes.StatusCode >= 300 && (int)dlRes.StatusCode < 400
                ? dlRes.Headers.Location?.ToString() ?? ""
                : "";
            if (apkUrl.Length == 0) throw new NoAppAssetException();

            return new AssetDetails(
                version,
                new List<KeyValuePair<string, string>> { new($"{appId}_{version}.apk", apkUrl) },
                new AppNames(author, appName),
                releaseDate,
                changeLog,
                new List<KeyValuePair<string, string>>(),
                null,
                null);
        }
    }

    // Token generation adapted (as upstream) from FuckCoolapkTokenV2.
    private static (string DeviceCode, string Token) GetToken()
    {
        var rand = new Random();
        string RandHexString(int n) =>
            string.Concat(Enumerable.Range(0, n).Select(_ => rand.Next(256).ToString("x2"))).ToUpperInvariant();
        string RandMacAddress() =>
            string.Join(':', Enumerable.Range(0, 6).Select(_ => rand.Next(256).ToString("x2")));

        var aid = RandHexString(16);
        var mac = RandMacAddress();
        const string manufactor = "Google";
        const string brand = "Google";
        const string model = "Pixel 5a";
        const string buildNumber = "SQ1D.220105.007";

        var deviceCode = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{aid}; ; ; {mac}; {manufactor}; {brand}; {model}; {buildNumber}"));

        var timeStamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        var base64TimeStamp = Convert.ToBase64String(Encoding.ASCII.GetBytes(timeStamp));
        string Md5Hex(string s) => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(s))).ToLowerInvariant();
        var md5TimeStamp = Md5Hex(timeStamp);
        var md5DeviceCode = Md5Hex(deviceCode);

        var token = $"token://com.coolapk.market/dcf01e569c1e3db93a3d0fcf191a622c?{md5TimeStamp}${md5DeviceCode}&com.coolapk.market";
        var base64Token = Convert.ToBase64String(Encoding.ASCII.GetBytes(token));
        var md5Base64Token = Md5Hex(base64Token);
        var md5Token = Md5Hex(token);

        var bcryptSalt = "$2a$10$" + base64TimeStamp.Substring(0, 14) + "/" + md5Token.Substring(0, 6) + "u";
        var bcryptResult = BCrypt.Net.BCrypt.HashPassword(md5Base64Token, bcryptSalt);
        var reBcryptResult = "$2y" + bcryptResult.Substring(3);
        var finalToken = "v2" + Convert.ToBase64String(Encoding.ASCII.GetBytes(reBcryptResult));

        return (deviceCode, finalToken);
    }
}
