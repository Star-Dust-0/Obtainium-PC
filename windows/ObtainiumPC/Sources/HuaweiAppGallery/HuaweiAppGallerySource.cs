using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ObtainiumPC.Sources.HuaweiAppGallery;

/// <summary>
/// Port of lib/app_sources/huaweiappgallery.dart: Huawei AppGallery store API
/// with the front2 handshake session (zone-based host selection, signed
/// requests). Windows mapping: the session is cached in memory only (upstream
/// also persists it in shared prefs). Track-only on Windows.
/// </summary>
public sealed class HuaweiAppGallerySource : AppSourceBase
{
    private const string ApiPath = "/hwmarket/api/clientApi";
    private const string UserAgentValue = "HiSpace##16.5.1.301##google##Pixel 8 Pro";
    private const string ClientVersion = "16.5.1";
    private const string ClientVersionCode = "160501301";
    private const string HostCN = "store-drcn.hispace.dbankcloud.com";
    private const string HostAsia = "store-dra.hispace.dbankcloud.com";
    private const string HostEU = "store-dre.hispace.dbankcloud.com";
    private const string HostRU = "store-drru.hispace.dbankcloud.ru";

    // DR2 (Asia/Africa/Latin America) country codes; anything else uses Europe.
    private static readonly HashSet<string> Dr2Zones =
        "AE AF AG AI AM AO AQ AR AS AW AZ BB BD BF BH BI BJ BL BM BN BO BR BS BT BV BW BY BZ CC CD CF CG CI CK CL CM CO CR CU CV CX DJ DM DO DZ EC EG EH ER ET FJ FK FM GA GD GE GF GH GM GN GP GQ GS GT GU GW GY HK HM HN HT ID IN IO IQ JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LK LR LS LY MA MG MH ML MM MN MO MP MQ MR MS MU MV MW MX MY MZ NA NC NE NF NG NI NP NR NU OM PA PE PF PG PH PK PN PR PS PW PY QA RE RW SA SB SC SD SG SH SL SN SO SR SS ST SV SY SZ TC TD TF TG TH TJ TK TL TM TN TO TT TV TW TZ UG UY UZ VE VG VI VN VU WF WS YE YT ZA ZM ZW"
            .Split(' ').ToHashSet();

    private sealed record Session(string Host, string Sign, string DeviceId, DateTimeOffset CreatedAt)
    {
        public bool IsUsable => DateTimeOffset.Now - CreatedAt <= TimeSpan.FromHours(24);
    }

    private static Session? _session;

    internal static void ResetSession() => _session = null;

    private readonly HttpClient _http;

    public HuaweiAppGallerySource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public override string Name => "Huawei AppGallery";
    public override IReadOnlyList<string> Hosts => new[]
        { "appgallery.huawei.com", "appgallery.cloud.huawei.com", "appgallery.huawei.ru" };
    public override bool SupportsInstall => false;
    public override bool CanSearch => true;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, @"(www\.)?", @"(/#)?/(app|appdl)/[^/]+");

    private static string HostForZone(string? zone) => zone switch
    {
        "CN" => HostCN,
        "RU" => HostRU,
        _ when Dr2Zones.Contains(zone ?? "") => HostAsia,
        _ => HostEU,
    };

    /// POSTs a sorted-key form body to the store API.
    private async Task<JsonElement> ClientApiPostAsync(
        string host, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var body = string.Join('&', parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value).Replace("%20", "+")}"));
        var apiUrl = $"https://{host}{ApiPath}";
        var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgentValue);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var res = await _http.SendAsync(req, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, apiUrl);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new ObtainiumException("Unexpected store API response.");
        return doc.RootElement.Clone();
    }

    private static Dictionary<string, string> CommonParams(string deviceId) => new()
    {
        ["ver"] = "1.1",
        ["locale"] = "en_US",
        ["serviceType"] = "0",
        ["ts"] = DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        ["net"] = "1",
        ["brand"] = "google",
        ["manufacturer"] = "Google",
        ["subBrand"] = "0",
        ["deviceId"] = deviceId,
        ["deviceIdType"] = "9",
    };

    private Task<JsonElement> Front2Async(
        string host, int needServiceZone, string deviceId, CancellationToken cancellationToken)
    {
        var parameters = CommonParams(deviceId);
        parameters["method"] = "client.front2";
        parameters["version"] = ClientVersion;
        parameters["versionCode"] = ClientVersionCode;
        parameters["packageName"] = "com.huawei.appmarket";
        parameters["zone"] = "1";
        parameters["phoneType"] = "Pixel 8 Pro";
        parameters["firmwareVersion"] = "16";
        parameters["isFirstLaunch"] = "1";
        parameters["oobe"] = "0";
        parameters["needServiceZone"] = needServiceZone.ToString(CultureInfo.InvariantCulture);
        return ClientApiPostAsync(host, parameters, cancellationToken);
    }

    /// Runs the front2 handshake once and caches the session in memory.
    private async Task<Session> EnsureSessionAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _session is { IsUsable: true } session)
            return session;

        var deviceId = string.Concat(Enumerable.Range(0, 64).Select(_ => Random.Shared.Next(16).ToString("x")));

        var probe = await Front2Async(HostEU, 1, deviceId, cancellationToken);
        var host = HostForZone(probe.TryGetProperty("serviceZone", out var sz) ? sz.ToString() : null);
        var sign = probe.TryGetProperty("sign", out var s) ? s.ToString() : null;
        if (forceRefresh || string.IsNullOrEmpty(sign))
        {
            var second = await Front2Async(host, 0, deviceId, cancellationToken);
            sign = second.TryGetProperty("sign", out var s2) ? s2.ToString() : null;
        }
        if (string.IsNullOrEmpty(sign))
            throw new ObtainiumException($"Store handshake returned no sign ({host}).");
        return _session = new Session(host, sign, deviceId, DateTimeOffset.Now);
    }

    /// Fetches app details from the store API. Returns null on any failure.
    private async Task<JsonElement?> FetchAppDetailAsync(string cId, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var session = await EnsureSessionAsync(forceRefresh: attempt > 0, cancellationToken);
                var parameters = CommonParams(session.DeviceId);
                parameters["method"] = "client.appDetailById";
                parameters["sign"] = session.Sign;
                parameters["id"] = cId;
                var resp = await ClientApiPostAsync(session.Host, parameters, cancellationToken);
                var rtnCode = resp.TryGetProperty("rtnCode", out var rc) ? rc.ToString() : "";
                if (rtnCode == "0")
                {
                    if (resp.TryGetProperty("detailInfo", out var detailInfo)
                        && detailInfo.ValueKind == JsonValueKind.Array
                        && detailInfo.GetArrayLength() > 0)
                    {
                        var info = detailInfo[0];
                        if (info.ValueKind == JsonValueKind.Object
                            && info.TryGetProperty("versionName", out var vn) && !string.IsNullOrEmpty(vn.ToString())
                            && info.TryGetProperty("url", out var u) && !string.IsNullOrEmpty(u.ToString()))
                            return info.Clone();
                    }
                    // Unknown/removed app or unexpected payload.
                    return null;
                }
                // Non-zero rtnCode: possibly an expired sign. Refresh once and retry.
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var cId = standardUrl.Split('/').Last();
        var info = await FetchAppDetailAsync(cId, cancellationToken);
        // The store API is the only source of the real version name; the legacy
        // appdl redirect only exposes a build timestamp (#3247), so fail instead.
        if (info is not { } appInfo)
            throw new ObtainiumException("Huawei AppGallery store API failed.");

        var version = appInfo.GetProperty("versionName").ToString();
        var package = appInfo.TryGetProperty("package", out var p) ? p.ToString() : null;
        var appName = appInfo.TryGetProperty("name", out var n) ? n.ToString() : null;
        var developer = appInfo.TryGetProperty("developer", out var d) ? d.ToString() : null;
        DateTimeOffset? releaseDate = null;
        if (appInfo.TryGetProperty("releaseDate", out var rd)
            && DateTimeOffset.TryParse(rd.ToString(), null, DateTimeStyles.AssumeUniversal, out var parsed))
            releaseDate = parsed;
        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>>
            {
                new($"{(string.IsNullOrEmpty(package) ? cId : package)}.apk", appInfo.GetProperty("url").ToString()),
            },
            new AppNames(
                !string.IsNullOrEmpty(developer) ? developer : Name,
                !string.IsNullOrEmpty(appName) ? appName : (package ?? "App")),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query,
        IReadOnlyDictionary<string, JsonElement> querySettings,
        CancellationToken cancellationToken = default)
    {
        string? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var session = await EnsureSessionAsync(forceRefresh: attempt > 0, cancellationToken);
            var parameters = CommonParams(session.DeviceId);
            parameters["method"] = "client.getTabDetail";
            parameters["sign"] = session.Sign;
            parameters["uri"] = $"searchApp|{query}";
            parameters["maxResults"] = "25";
            parameters["reqPageNum"] = "1";
            parameters["isSupportPage"] = "1";
            var resp = await ClientApiPostAsync(session.Host, parameters, cancellationToken);
            var rtnCode = resp.TryGetProperty("rtnCode", out var rc) ? rc.ToString() : "";
            if (rtnCode == "0")
            {
                var results = new Dictionary<string, IReadOnlyList<string>>();
                if (resp.TryGetProperty("layoutData", out var layoutData)
                    && layoutData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var layout in layoutData.EnumerateArray())
                    {
                        if (layout.ValueKind != JsonValueKind.Object
                            || !layout.TryGetProperty("dataList", out var dataList)
                            || dataList.ValueKind != JsonValueKind.Array)
                            continue;
                        foreach (var item in dataList.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            var app = item.TryGetProperty("appInfo", out var ai)
                                && ai.ValueKind == JsonValueKind.Object ? ai : item;
                            var appId = app.TryGetProperty("appid", out var id1) ? id1.ToString() : null;
                            if (string.IsNullOrEmpty(appId))
                                appId = app.TryGetProperty("appId", out var id2) ? id2.ToString() : null;
                            var appName = app.TryGetProperty("name", out var an) ? an.ToString() : null;
                            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appName)) continue;
                            var package = app.TryGetProperty("package", out var pk) ? pk.ToString() : "";
                            var description = new List<string> { appName };
                            if (!string.IsNullOrEmpty(package)) description.Add(package);
                            results[$"https://{Hosts[0]}/app/{appId}"] = description;
                        }
                    }
                }
                return results;
            }
            var rtnDesc = resp.TryGetProperty("rtnDesc", out var rd) ? rd.ToString() : "";
            lastError = $"rtnCode={rtnCode} rtnDesc={rtnDesc}";
        }
        throw new ObtainiumException($"Huawei AppGallery search failed: {lastError}");
    }
}
