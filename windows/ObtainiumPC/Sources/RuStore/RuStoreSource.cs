using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ObtainiumPC.Sources.RuStore;

/// <summary>
/// Port of lib/app_sources/rustore.dart: RuStore via backapi.rustore.ru with an
/// HMAC-signed device session. Windows mappings, all matching upstream's own
/// fallbacks when Android device info is unavailable: supportedAbis is
/// ["arm64-v8a"], SDK 36, screen density 420, device type "mobile", and
/// charset-detected bodies are decoded as UTF-8. Track-only on Windows.
/// </summary>
public sealed class RuStoreSource : AppSourceBase
{
    private const string AppInfoUrl = "https://backapi.rustore.ru/applicationData/overallInfo";
    private const string SearchUrl = "https://backapi.rustore.ru/applicationData/apps";
    private const string DownloadLinkUrl = "https://backapi.rustore.ru/v3/showcase/apps/download-link";
    private const string NonceUrl = "https://api.rustore.ru/v1/secure/nonce";

    private static readonly byte[] HmacKey = Convert.FromBase64String("K+eeiCbnVFnZ71KEVal0g5siHaX6v6drh8upeLgEPoU=");
    private static readonly byte[] ApkCertSha256 = Convert.FromBase64String("Zh8ggo73gN4LebxZ8mowhkMWNV8w5Pkc+hSiB5GDmRQ=");

    private const string DeviceManufacturer = "Google";
    private const string DeviceModelName = "Pixel 8 Pro";
    private const string DeviceHardware = "husky";
    private const string FirmwareVer = "16";
    private const string AndroidSdkVer = "36";
    private const string FirmwareLang = "ru";
    private const string RuStoreVerCode = "1105002";
    private const string UserAgentValue =
        "RuStore/1.105.0.2 (Android " + FirmwareVer + "; SDK " + AndroidSdkVer + "; arm64-v8a; "
        + DeviceManufacturer + " " + DeviceModelName + "; " + FirmwareLang + ")";

    // Upstream keeps these static; shared across source instances.
    private static (string DeviceId, string Signature)? _session;

    private readonly HttpClient _http;

    public RuStoreSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public override string Name => "RuStore";
    public override IReadOnlyList<string> Hosts => new[] { "rustore.ru" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;
    public override bool ShowReleaseDateAsVersionToggle => true;
    public override bool CanSearch => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, @"(www\.)?", @"/catalog/app/+[^/]+");

    private static Dictionary<string, string> DeviceHeaders(string? deviceId, string? signature)
    {
        var headers = new Dictionary<string, string>
        {
            ["firmwareVer"] = FirmwareVer,
            ["androidSdkVer"] = AndroidSdkVer,
            ["deviceManufacturerName"] = DeviceManufacturer,
            ["deviceModelName"] = DeviceModelName,
            ["deviceModel"] = DeviceManufacturer + " " + DeviceModelName,
            ["firmwareLang"] = FirmwareLang,
            ["ruStoreVerCode"] = RuStoreVerCode,
            ["deviceType"] = "mobile", // Windows mapping: no isTV setting.
            ["User-Agent"] = UserAgentValue,
        };
        if (deviceId != null) headers["deviceId"] = deviceId;
        if (signature != null) headers["X-Client-Signature"] = signature;
        return headers;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method, string url, string? jsonBody, CancellationToken cancellationToken)
    {
        var needsSignature = url.StartsWith(AppInfoUrl, StringComparison.Ordinal)
            || url.StartsWith(DownloadLinkUrl, StringComparison.Ordinal);
        var session = needsSignature ? await GetSecureSessionAsync(cancellationToken) : _session;
        var headers = DeviceHeaders(session?.DeviceId ?? RandomDeviceId(), needsSignature ? session?.Signature : null);
        var req = new HttpRequestMessage(method, url)
        {
            Content = jsonBody == null ? null : new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        foreach (var (key, value) in headers)
            req.Headers.TryAddWithoutValidation(key, value);
        return req;
    }

    private static JsonElement DecodeJsonBody(byte[] bytes)
    {
        // Windows mapping: UTF-8 assumed (upstream runs charset detection).
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        return doc.RootElement.Clone();
    }

    /// Original: apkUrlsFromDownloadUrls. One URL is an .apk beside its .zip;
    /// several are a base APK plus config splits joined for the pipeline.
    internal static List<KeyValuePair<string, string>> ApkUrlsFromDownloadUrls(List<string> urls)
    {
        if (urls.Count == 1)
            return AssetFilterService.GetAssetUrlsFromUrls(new[]
            {
                System.Text.RegularExpressions.Regex.Replace(urls[0], @"\.zip$", ".apk"),
            });
        var key = AssetFilterService.GetAssetUrlsFromUrls(new[] { urls[0] })[0].Key;
        return new List<KeyValuePair<string, string>> { new(key, string.Join('\n', urls)) };
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appId = new Uri(standardUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        using var overallInfoResponse = await SourceRequestWithSessionRetryAsync(
            $"{AppInfoUrl}/{appId}", null, cancellationToken);
        if ((int)overallInfoResponse.StatusCode >= 400)
            throw new ObtainiumHttpException((int)overallInfoResponse.StatusCode, standardUrl);
        var decoded = DecodeJsonBody(await overallInfoResponse.Content.ReadAsByteArrayAsync(cancellationToken));
        JsonElement appDetails = default;
        var hasAppDetails = decoded.ValueKind == JsonValueKind.Object
            && decoded.TryGetProperty("body", out var body)
            && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("appId", out _)
            && (appDetails = body).ValueKind == JsonValueKind.Object;
        if (!hasAppDetails) throw new NoReleasesException();

        var appName = appDetails.TryGetProperty("appName", out var an) ? an.ToString() : "App";
        var author = appDetails.TryGetProperty("companyName", out var cn) ? cn.ToString() : Name;
        var dateStr = appDetails.TryGetProperty("appVerUpdatedAt", out var d) ? d.ToString() : null;
        var version = appDetails.TryGetProperty("versionName", out var v) ? v.ToString() : null;
        string? changeLog = appDetails.TryGetProperty("whatsNew", out var wn) ? wn.ToString() : null;
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
        DateTimeOffset? relDate = null;
        if (dateStr != null
            && DateTimeOffset.TryParse(dateStr, null, DateTimeStyles.AssumeUniversal, out var parsed))
            relDate = parsed;

        var postBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["appId"] = appDetails.GetProperty("appId").ToString(),
            ["firstInstall"] = true,
            // Device profile so RuStore returns split APKs when there is no
            // universal APK (all three fields required).
            ["withoutSplits"] = false,
            ["supportedAbis"] = new[] { "arm64-v8a" }, // Windows mapping: upstream's own default.
            ["sdkVersion"] = 36,
            ["screenDensity"] = 420, // Upstream's no-display fallback density.
        });
        using var downloadLinksResponse = await SourceRequestWithSessionRetryAsync(
            DownloadLinkUrl, postBody, cancellationToken);
        if ((int)downloadLinksResponse.StatusCode >= 400)
            throw new ObtainiumHttpException((int)downloadLinksResponse.StatusCode, DownloadLinkUrl);
        var downloadDetails = DecodeJsonBody(await downloadLinksResponse.Content.ReadAsByteArrayAsync(cancellationToken));

        var urls = new List<string>();
        if (downloadDetails.ValueKind == JsonValueKind.Object
            && downloadDetails.TryGetProperty("downloadUrls", out var downloadUrls)
            && downloadUrls.ValueKind == JsonValueKind.Array)
        {
            urls = downloadUrls.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.TryGetProperty("url", out var u) ? u.GetString() : null)
                .Where(u => !string.IsNullOrEmpty(u))
                .Cast<string>()
                .ToList();
        }
        if (urls.Count == 0)
        {
            // Aggregated cards come from an external source; RuStore hosts no APK (#3298).
            if (appDetails.TryGetProperty("aggregatorInfo", out var agg) && agg.ValueKind == JsonValueKind.Object)
                throw new ObtainiumException("RuStore aggregated app: no hosted APK (RUSTORE_AGGREGATED_APP)");
            throw new NoAppAssetException();
        }

        return new AssetDetails(
            version,
            ApkUrlsFromDownloadUrls(urls),
            new AppNames(author, appName),
            relDate,
            changeLog,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement> querySettings,
        CancellationToken cancellationToken = default)
    {
        var url = $"{SearchUrl}?query={Uri.EscapeDataString(query)}&pageNumber=0&pageSize=20";
        using var res = await _http.SendAsync(await BuildRequestAsync(HttpMethod.Get, url, null, cancellationToken), cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, url);
        var decoded = DecodeJsonBody(await res.Content.ReadAsByteArrayAsync(cancellationToken));
        var results = new Dictionary<string, IReadOnlyList<string>>();
        if (decoded.ValueKind == JsonValueKind.Object
            && decoded.TryGetProperty("body", out var body)
            && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var app in content.EnumerateArray())
            {
                if (app.ValueKind != JsonValueKind.Object) continue;
                var packageName = app.TryGetProperty("packageName", out var pn) ? pn.ToString() : null;
                var appName = app.TryGetProperty("appName", out var an) ? an.ToString() : null;
                if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(appName)) continue;
                results[$"https://rustore.ru/catalog/app/{packageName}"] = new List<string> { appName, packageName };
            }
        }
        return results;
    }

    private static async Task<(string DeviceId, string Signature)?> GenerateSecureSessionAsync(
        HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            var deviceId = RandomDeviceId();
            var req = new HttpRequestMessage(HttpMethod.Post, NonceUrl) { Content = new StringContent("") };
            foreach (var (key, value) in DeviceHeaders(deviceId, null))
                req.Headers.TryAddWithoutValidation(key, value);
            using var response = await http.SendAsync(req, cancellationToken);
            if ((int)response.StatusCode != 200) return null;
            var decoded = DecodeJsonBody(await response.Content.ReadAsByteArrayAsync(cancellationToken));
            var nonceB64 = decoded.ValueKind == JsonValueKind.Object
                && decoded.TryGetProperty("nonce", out var n) ? n.GetString() : null;
            if (nonceB64 == null) return null;
            // signature = base64(HMAC-SHA256(KEY, base64decode(nonce) || certSha256))
            var nonce = Convert.FromBase64String(nonceB64);
            using var hmac = new HMACSHA256(HmacKey);
            var digest = hmac.ComputeHash(nonce.Concat(ApkCertSha256).ToArray());
            return _session = (deviceId, Convert.ToBase64String(digest));
        }
        catch
        {
            return null;
        }
    }

    private async Task<(string DeviceId, string Signature)?> GetSecureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session != null) return _session;
        return await GenerateSecureSessionAsync(_http, cancellationToken);
    }

    /// sourceRequest with 419 (session rejected/missing) handling.
    private async Task<HttpResponseMessage> SourceRequestWithSessionRetryAsync(
        string url, string? jsonBody, CancellationToken cancellationToken)
    {
        var method = jsonBody == null ? HttpMethod.Get : HttpMethod.Post;
        var response = await _http.SendAsync(await BuildRequestAsync(method, url, jsonBody, cancellationToken), cancellationToken);
        if ((int)response.StatusCode == 419
            && await GenerateSecureSessionAsync(_http, cancellationToken) != null)
        {
            response.Dispose();
            response = await _http.SendAsync(await BuildRequestAsync(method, url, jsonBody, cancellationToken), cancellationToken);
        }
        return response;
    }

    internal static string RandomDeviceId()
    {
        static int I32(int x) => unchecked(x);
        static int JavaStringHashCode(string s)
        {
            var h = 0;
            foreach (var ch in s)
                h = unchecked(31 * h + ch);
            return h;
        }

        static string DeviceIdSuffix()
        {
            var m = JavaStringHashCode(DeviceManufacturer);
            var mo = JavaStringHashCode(DeviceModelName);
            var h = JavaStringHashCode(DeviceHardware);
            var d = JavaStringHashCode(DeviceHardware);
            return I32(d + I32(unchecked((h + I32(unchecked((mo + I32(m * 31)) * 31))) * 31))).ToString(CultureInfo.InvariantCulture);
        }

        var androidId = string.Concat(Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(256).ToString("x2")));
        return $"{androidId}-{DeviceIdSuffix()}";
    }
}
