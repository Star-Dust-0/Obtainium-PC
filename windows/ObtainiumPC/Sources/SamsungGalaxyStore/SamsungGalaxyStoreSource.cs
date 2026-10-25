using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.SamsungGalaxyStore;

/// <summary>
/// Port of lib/app_sources/samsunggalaxystore.dart: Samsung Galaxy Store via
/// the VAS stub plus the ODS protocol (endpoint discovery, download grants,
/// store details). Windows mapping: SDK version is upstream's "37" fallback
/// (no Android device). Track-only on Windows.
/// </summary>
public sealed class SamsungGalaxyStoreSource : AppSourceBase
{
    private const string ChinaEndpoint = "https://cn-ms.galaxyappstore.com/ods.as";
    private const string GlobalEndpoint = "https://us-odc.samsungapps.com/ods.as";
    private const string GlobalHub = "https://hub-odc.samsungapps.com/ods.as";

    private static readonly Dictionary<string, string> GlobalCountries = new()
    {
        ["us-odc.samsungapps.com"] = "USA",
        ["il-odc.samsungapps.com"] = "ISR",
    };

    private static readonly HashSet<string> CriticalFields = new()
    {
        "GUID", "appId", "productID", "productId", "productName", "version",
        "versionName", "versionCode", "realContentsSize", "contentsSize",
        "contentSize", "downLoadURI", "downloadURI", "needToLogin",
        "installableYN", "linkProductYn", "countryURL", "countryCode", "MCC",
        "lastUpdateDate", "updateDescription", "returnCode", "errorCode",
        "errorString",
    };

    private sealed class OdsRejectedException : ObtainiumSourceException
    {
        public OdsRejectedException(string message) : base(message) { }
    }

    private readonly HttpClient _http;

    public SamsungGalaxyStoreSource(HttpClient? http = null)
    {
        _http = http ?? ObtainiumHttp.CreateClient();
    }

    public override string Name => "Samsung Galaxy Store";
    public override IReadOnlyList<string> Hosts => new[]
    {
        "galaxystore.samsung.com", "apps.samsung.com", "apps.samsung.cn",
        "galaxyappstore.com", "apps.galaxyappstore.com",
    };
    public override bool SupportsInstall => false;
    public override bool ShowReleaseDateAsVersionToggle => true;

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Device options", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "deviceId", Label = "Device model", Kind = Forms.OptionFormItemKind.Text, Hint = "SM-S948B" } },
            new Forms.OptionFormItem[] { new() { Key = "csc", Label = "CSC code", Kind = Forms.OptionFormItemKind.Text, Hint = "DBT" } },
        };

    private static bool IsChina(IReadOnlyDictionary<string, string> device) =>
        device["csc"] == "CHC" && device["mcc"] == "460";

    private static string NewIdentity() =>
        string.Concat(Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(256).ToString("x2")));

    internal static DateTimeOffset? StoreDate(string? value)
    {
        var match = Regex.Match(value ?? "", @"^(\d{4});(\d{2});(\d{2});$");
        if (!match.Success) return null;
        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        try
        {
            var date = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
            return year > 0 && date.Year == year && date.Month == month && date.Day == day ? date : null;
        }
        catch { return null; }
    }

    private static Dictionary<string, string> XmlFields(
        byte[] bodyBytes, string rootName, string? requestId = null)
    {
        try
        {
            if (bodyBytes.Length == 0 || bodyBytes.Length > 2000000) throw new FormatException();
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit };
            XDocument document;
            using (var reader = System.Xml.XmlReader.Create(new MemoryStream(bodyBytes), settings))
                document = XDocument.Load(reader);
            var root = document.Root ?? throw new FormatException();
            if (root.Name.LocalName != rootName) throw new FormatException();

            var fields = new Dictionary<string, string>();
            IEnumerable<XElement> values;
            if (rootName == "SamsungProtocol")
            {
                var responses = root.Elements("response").ToList();
                if (responses.Count != 1) throw new FormatException();
                var protocol = responses[0];
                if (protocol.Attribute("id")?.Value != requestId) throw new FormatException();
                fields["returnCode"] = protocol.Attribute("returnCode")?.Value ?? "";
                var errors = protocol.Elements("errorInfo").ToList();
                if (errors.Count != 1) throw new FormatException();
                var strings = errors[0].Elements("errorString").ToList();
                if (strings.Count != 1 || strings[0].HasElements) throw new FormatException();
                fields["errorCode"] = strings[0].Attribute("errorCode")?.Value ?? "";
                fields["errorString"] = strings[0].Value.Trim();
                var lists = protocol.Elements("list").ToList();
                if (lists.Count > 1) throw new FormatException();
                if (lists.Count == 1 && lists[0].Elements().Any(node =>
                        node.Name.LocalName != "value"
                        && CriticalFields.Contains(node.Attribute("name")?.Value ?? node.Name.LocalName)))
                    throw new FormatException();
                // Complex extList branches contain unrelated repeated display components.
                values = lists.Count == 0 ? Enumerable.Empty<XElement>() : lists[0].Elements("value");
            }
            else
            {
                values = root.Elements();
            }
            foreach (var node in values)
            {
                if (node.HasElements) throw new FormatException();
                var key = node.Attribute("name")?.Value ?? node.Name.LocalName;
                if (fields.ContainsKey(key)) throw new FormatException();
                fields[key] = key == "updateDescription" ? node.Value : node.Value.Trim();
            }
            return fields;
        }
        catch (Exception e) when (e is FormatException or System.Xml.XmlException)
        {
            throw new ObtainiumException("Unexpected store API response.");
        }
    }

    private static Uri DownloadUri(string raw, bool forLinkedProduct = false)
    {
        var valid = Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && uri.Scheme == "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.Port == 443
            && string.IsNullOrEmpty(uri.Fragment)
            && !Regex.IsMatch(raw, @"[\s\\]")
            && uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length > 0
            && (new[] { "samsungapps.com", "galaxyappstore.com" }.Any(host =>
                    uri.Host == host || uri.Host.EndsWith("." + host))
                || (forLinkedProduct && uri.Host == "auto-dd.myapp.com"));
        if (!valid) throw new NoAppAssetException();
        return uri!;
    }

    private async Task<Dictionary<string, string>> OdsRequestAsync(
        string method,
        string requestId,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> device,
        string identity,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var url = $"{endpoint}?reqId={requestId}&ot=01&ct=B";
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                OdsEnvelope(method, requestId, parameters, device, identity),
                Encoding.UTF8, "text/plain"),
        };
        req.Headers.TryAddWithoutValidation("Accept", "image/webp");
        using var response = await _http.SendAsync(req, cancellationToken);
        var status = (int)response.StatusCode;
        if (status != 200 && (status < 400 || status > 599))
            throw new ObtainiumException("Unexpected store API response.");
        if (status != 200)
        {
            if (status == 429) throw new ObtainiumHttpException(429, url);
            throw new OdsRejectedException($"HTTP {status}");
        }
        var fields = XmlFields(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            "SamsungProtocol", requestId);
        if (!Regex.IsMatch(fields.GetValueOrDefault("errorCode", ""), @"^-?\d{1,19}$")
            || !Regex.IsMatch(fields.GetValueOrDefault("returnCode", ""), @"^\d{1,19}$"))
            throw new ObtainiumException("Unexpected store API response.");
        if (fields["errorCode"] != "0")
            throw new OdsRejectedException("Samsung Galaxy Store API error.");
        if (fields["returnCode"] != "0")
            throw new ObtainiumException("Unexpected store API response.");
        if (fields.GetValueOrDefault("errorString", "").ToLowerInvariant() is not ("" or "success"))
            throw new ObtainiumException("Unexpected store API response.");
        return fields;
    }

    private static string OdsEnvelope(
        string method,
        string requestId,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> device,
        string identity)
    {
        // Samsung's session/transaction IDs use China standard time.
        var now = DateTimeOffset.UtcNow.AddHours(8);
        var language = IsChina(device) ? "zh_CN" : "en_US";
        string Pad(int value) => value.ToString("D2", CultureInfo.InvariantCulture);
        var hour = $"{now.Year}{Pad(now.Month)}{Pad(now.Day)}{Pad(now.Hour)}";
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}{hour}GalaxyApps"))).ToLowerInvariant();
        var transaction = $"{now.Day}{digest.Substring(0, 7)}";

        static string E(string value) => System.Security.SecurityElement.Escape(value) ?? "";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<SamsungProtocol");
        var attributes = new (string Key, string Value)[]
        {
            ("networkType", "0"), ("version2", "0"), ("lang", language),
            ("openApiVersion", device["sdkVer"]), ("deviceModel", device["deviceId"]),
            ("deviceMakerName", "samsung"), ("deviceMakerType", "0"),
            ("mcc", device["mcc"]), ("mnc", device["mnc"]), ("csc", device["csc"]),
            ("odcVersion", "4.6.11.4"),
            ("storeFilter", $"themeDeviceModel={device["deviceId"]}_TM"),
            ("supportFeature", ""), ("version", "7.9"), ("filter", "1"),
            ("odcType", "01"), ("storeMode", "0"), ("cacheVersion", "1"),
            ("systemId", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
            ("sessionId", $"{transaction}{hour}{Pad(now.Minute)}"),
            ("logId", identity),
            ("deviceFeature", $"locale={language}||abi32=armeabi-v7a:armeabi||abi64=arm64-v8a"),
            ("userMode", "0"), ("asaaMode", "0"),
        };
        foreach (var (key, value) in attributes)
            sb.Append($" {key}=\"{E(value)}\"");
        sb.Append('>');
        sb.Append($"<request name=\"{E(method)}\" id=\"{E(requestId)}\" numParam=\"{parameters.Count}\" transactionId=\"{E(transaction)}\">");
        foreach (var (key, value) in parameters)
            sb.Append($"<param name=\"{E(key)}\">{E(value)}</param>");
        sb.Append("</request></SamsungProtocol>");
        return sb.ToString();
    }

    private async Task<string> DiscoverEndpointAsync(
        IReadOnlyDictionary<string, string> device, string identity, CancellationToken cancellationToken)
    {
        var china = IsChina(device);
        var fallback = china ? ChinaEndpoint : GlobalEndpoint;
        try
        {
            var fields = await OdsRequestAsync(
                "countrySearchEx", "2300",
                new Dictionary<string, string>
                {
                    ["accountCountry"] = "",
                    ["accountMcc"] = "",
                    ["latestCountryCode"] = device["mcc"],
                    ["whoAmI"] = "odc",
                },
                device, identity, china ? ChinaEndpoint : GlobalHub, cancellationToken);
            var raw = fields.GetValueOrDefault("countryURL", "");
            var uri = Uri.TryCreate(raw, UriKind.Absolute, out var u) ? u : null;
            var country = china ? "CHN" : (uri != null ? GlobalCountries.GetValueOrDefault(uri.Host) : null);
            if (uri == null
                || (uri.Scheme != "http" && uri.Scheme != "https")
                || (china && uri.Host != "cn-ms.galaxyappstore.com")
                || country == null
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.Port != (uri.Scheme == "http" ? 80 : 443)
                || uri.AbsolutePath != "/ods.as"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || Regex.IsMatch(raw, @"[\s\\]")
                || fields.GetValueOrDefault("countryCode") != country
                || fields.GetValueOrDefault("MCC") != device["mcc"])
                return fallback;
            return $"https://{uri.Host}/ods.as";
        }
        catch (ObtainiumHttpException) { throw; }
        catch { return fallback; }
    }

    private async Task<AssetDetails> WithStoreDetailsAsync(
        AssetDetails result,
        string packageName,
        string productId,
        int versionCode,
        int size,
        IReadOnlyDictionary<string, string> device,
        string identity,
        string endpoint,
        CancellationToken cancellationToken)
    {
        bool Matches(IReadOnlyDictionary<string, string> fields) =>
            fields.GetValueOrDefault("GUID") == packageName
            && fields.GetValueOrDefault("productID") == productId
            && fields.GetValueOrDefault("version") == result.Version
            && (int.TryParse(fields.GetValueOrDefault("versionCode", ""), out var vc) ? vc : (int?)null) == versionCode
            && (int.TryParse(fields.GetValueOrDefault("realContentsSize", ""), out var rs) ? rs : (int?)null) == size;

        var parameters = new Dictionary<string, string>
        {
            ["GUID"] = packageName, ["productID"] = productId,
            ["imei"] = identity, ["extuk"] = identity, ["stduk"] = identity,
        };
        var mainParams = new Dictionary<string, string>(parameters)
        {
            ["productImgWidth"] = "135", ["productImgHeight"] = "135",
            ["lkAppIncludedYN"] = "Y", ["predeployed"] = "0", ["triggeredFrom"] = "detail",
        };
        try
        {
            var main = await OdsRequestAsync("guidProductDetailExMain", "2290", mainParams, device, identity, endpoint, cancellationToken);
            if (!Matches(main)) return result;
            var overview = await OdsRequestAsync("guidProductDetailExOverview", "2291",
                new Dictionary<string, string>(parameters)
                {
                    ["imgWidth"] = "1080", ["imgHeight"] = "1920",
                    ["runestoneYn"] = "N", ["userAge"] = "",
                },
                device, identity, endpoint, cancellationToken);
            if (overview.GetValueOrDefault("version") != result.Version
                || (int.TryParse(overview.GetValueOrDefault("realContentsSize", ""), out var ors) ? ors : (int?)null) != size
                || (overview.ContainsKey("GUID") && overview["GUID"] != packageName)
                || (overview.ContainsKey("productID") && overview["productID"] != productId)
                || (overview.ContainsKey("versionCode")
                    && (!int.TryParse(overview["versionCode"], out var ovc) || ovc != versionCode)))
                return result;
            // Overview omits package/code; stable main responses bind it to this release.
            var after = await OdsRequestAsync("guidProductDetailExMain", "2290", mainParams, device, identity, endpoint, cancellationToken);
            if (!Matches(after)) return result;
            var changeLog = overview.GetValueOrDefault("updateDescription");
            return result with
            {
                ReleaseDate = StoreDate(overview.GetValueOrDefault("lastUpdateDate")),
                ChangeLog = !string.IsNullOrWhiteSpace(changeLog) ? changeLog : null,
            };
        }
        catch
        {
            // Optional store details cannot invalidate an already authorized APK.
            return result;
        }
    }

    private async Task<AssetDetails> GetOdsDetailsAsync(
        string packageName,
        IReadOnlyDictionary<string, string> device,
        CancellationToken cancellationToken)
    {
        var identity = NewIdentity();
        var endpoint = await DiscoverEndpointAsync(device, identity, cancellationToken);
        var identifiers = new Dictionary<string, string>
        {
            ["imei"] = identity, ["extuk"] = identity, ["stduk"] = identity,
        };
        var metadataParams = new Dictionary<string, string>
        {
            ["mode"] = "directDownload", ["guid"] = packageName, ["productID"] = "",
            ["imei"] = identity, ["extuk"] = identity, ["stduk"] = identity,
            ["predeployed"] = "0", ["unifiedPaymentYN"] = "Y", ["lkAppIncludedYN"] = "Y",
            ["betaTestYN"] = "N", ["minorYN"] = "N", ["stateCode"] = "",
        };
        var metadata = await OdsRequestAsync(
            "getDownloadInfo", "2298", metadataParams, device, identity, endpoint, cancellationToken);
        var productId = metadata.GetValueOrDefault("productID", "");
        var version = metadata.GetValueOrDefault("version", "");
        var versionCode = int.TryParse(metadata.GetValueOrDefault("versionCode", ""), out var vci) ? vci : 0;
        int? metadataSize = metadata.ContainsKey("realContentsSize")
            ? (int.TryParse(metadata["realContentsSize"], out var ms) ? ms : (int?)null)
            : null;
        if (metadata.GetValueOrDefault("GUID") != packageName
            || !Regex.IsMatch(productId, @"^\d{1,30}$")
            || version.Length == 0
            || versionCode <= 0
            || (metadata.ContainsKey("realContentsSize") && (metadataSize == null || metadataSize <= 0))
            || (metadata.ContainsKey("linkProductYn")
                && metadata["linkProductYn"] is not ("0" or "1"))
            || metadata.GetValueOrDefault("needToLogin") is not ("0" or "1")
            || metadata.GetValueOrDefault("installableYN") is not ("Y" or "N"))
            throw new ObtainiumException("Unexpected store API response.");
        if (metadata["needToLogin"] == "1")
            throw new ObtainiumException("Samsung Galaxy Store login required.");
        if (metadata["installableYN"] != "Y") throw new NoAppAssetException();
        var linkedProduct = metadata.GetValueOrDefault("linkProductYn") == "1";
        if (linkedProduct && (!IsChina(device) || metadataSize == null))
            throw new NoAppAssetException();

        Task<Dictionary<string, string>> MirrorGrant() => OdsRequestAsync(
            "downloadInfoForTencent", "2801",
            new Dictionary<string, string>
            {
                ["GUID"] = packageName, ["stduk"] = identity, ["extuk"] = identity,
                ["tencentSource"] = "general", ["lastInterfaceName"] = "searchProductListEx2Notc",
            },
            device, identity, endpoint, cancellationToken);

        var authorizationParams = new Dictionary<string, string>
        {
            ["GUID"] = packageName, ["productID"] = productId,
            ["imei"] = identity, ["extuk"] = identity, ["stduk"] = identity,
            ["autoUpdateYN"] = "N", ["predeployed"] = "0", ["resumeYN"] = "N",
        };
        Dictionary<string, string> grant;
        var usedRestoreAuthorization = false;
        var usedMirror = false;
        if (linkedProduct)
        {
            usedMirror = true;
            grant = await MirrorGrant();
        }
        else
        {
            try
            {
                // Omitting versionCode requests a full package without assuming
                // a local installation.
                grant = await OdsRequestAsync("downloadEx2", "2311",
                    new Dictionary<string, string>(authorizationParams)
                    {
                        ["dowloadType"] = "new", ["deepLinkSource"] = "N",
                    },
                    device, identity, endpoint, cancellationToken);
            }
            catch (OdsRejectedException)
            {
                usedRestoreAuthorization = true;
                try
                {
                    grant = await OdsRequestAsync("downloadForRestore", "2316",
                        new Dictionary<string, string>(authorizationParams)
                        {
                            ["downloadType"] = "new", ["triggeredFrom"] = "DETAIL_PAGE",
                            ["deepLinkSource"] = "",
                        },
                        device, identity, endpoint, cancellationToken);
                }
                catch (OdsRejectedException)
                {
                    if (!IsChina(device) || metadataSize == null) throw;
                    usedMirror = true;
                    grant = await MirrorGrant();
                }
            }
        }
        var size = int.TryParse(grant.GetValueOrDefault("contentsSize", ""), out var cs) ? cs : 0;
        string? Field(Dictionary<string, string> f, string key) => f.TryGetValue(key, out var val) ? val : null;
        // Linked replies omit package/product echoes; bind their release to 2298.
        if (((!linkedProduct || grant.ContainsKey("productID")) && Field(grant, "productID") != productId)
            || (((usedMirror && !linkedProduct) || grant.ContainsKey("GUID")) && Field(grant, "GUID") != packageName)
            || ((!usedRestoreAuthorization || usedMirror || grant.ContainsKey("version")) && Field(grant, "version") != version)
            || ((!usedRestoreAuthorization || usedMirror || grant.ContainsKey("versionCode"))
                && (int.TryParse(grant.GetValueOrDefault("versionCode", ""), out var gvc) ? gvc : (int?)null) != versionCode)
            || size <= 0
            || (metadataSize != null && size != metadataSize))
            throw new ObtainiumException("Unexpected store API response.");
        // downLoadURI is the full APK, including universal 32n64 packages.
        var apkUrl = DownloadUri(grant.GetValueOrDefault("downLoadURI", ""), linkedProduct);
        return await WithStoreDetailsAsync(
            new AssetDetails(
                version,
                new List<KeyValuePair<string, string>> { new($"{packageName}.apk", apkUrl.ToString()) },
                new AppNames(Name,
                    !string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("productName"))
                        ? metadata["productName"].Trim()
                        : packageName),
                null, null, new List<KeyValuePair<string, string>>(), null, null),
            packageName, productId, versionCode, size, device, identity, endpoint, cancellationToken);
    }

    private async Task<AssetDetails> GetStubDetailsAsync(
        string packageName,
        IReadOnlyDictionary<string, string> device,
        CancellationToken cancellationToken)
    {
        // Keep the query parameter order identical to the Dart map literal.
        var vasUrl = "https://vas.samsungapps.com/stub/stubDownload.as"
            + $"?appId={Uri.EscapeDataString(packageName)}"
            + $"&deviceId={Uri.EscapeDataString(device["deviceId"])}"
            + $"&csc={Uri.EscapeDataString(device["csc"])}"
            + $"&sdkVer={Uri.EscapeDataString(device["sdkVer"])}"
            + $"&mcc={Uri.EscapeDataString(device["mcc"])}"
            + $"&mnc={Uri.EscapeDataString(device["mnc"])}"
            + "&systemId=1608665720954&abiType=64&extuk=0191d6627f38685f";

        using var response = await _http.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, vasUrl), cancellationToken);
        if ((int)response.StatusCode >= 400)
            throw new ObtainiumHttpException((int)response.StatusCode, vasUrl);
        var fields = XmlFields(
            await response.Content.ReadAsByteArrayAsync(cancellationToken), "result");
        if (fields.GetValueOrDefault("resultCode") != "1")
            throw new ObtainiumException("Samsung Galaxy Store API error.");
        var productId = fields.GetValueOrDefault("productId", "");
        var version = fields.GetValueOrDefault("versionName", "");
        var versionCode = int.TryParse(fields.GetValueOrDefault("versionCode", ""), out var vc) ? vc : 0;
        var size = int.TryParse(fields.GetValueOrDefault("contentSize", ""), out var sz) ? sz : 0;
        if (fields.GetValueOrDefault("appId") != packageName
            || !Regex.IsMatch(productId, @"^\d{1,30}$")
            || version.Length == 0 || versionCode <= 0 || size <= 0)
            throw new ObtainiumException("Unexpected store API response.");
        var apkUrl = DownloadUri(fields.GetValueOrDefault("downloadURI", ""));
        var result = new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new($"{packageName}.apk", apkUrl.ToString()) },
            new AppNames(Name,
                !string.IsNullOrEmpty(fields.GetValueOrDefault("productName"))
                    ? fields["productName"]
                    : packageName),
            null, null, new List<KeyValuePair<string, string>>(), null, null);
        var identity = NewIdentity();
        string endpoint;
        try
        {
            endpoint = await DiscoverEndpointAsync(device, identity, cancellationToken);
        }
        catch (ObtainiumHttpException)
        {
            return result;
        }
        return await WithStoreDetailsAsync(
            result, packageName, productId, versionCode, size, device, identity, endpoint, cancellationToken);
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var uri = new Uri(url);
        var validHosts = Hosts.Concat(Hosts.Select(h => "www." + h)).ToList();
        if (!validHosts.Contains(uri.Host)) throw new InvalidUrlException(Name);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var appId = query["appId"];
        if (string.IsNullOrEmpty(appId))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            appId = segments.Length > 0 ? segments[segments.Length - 1] : null;
        }
        if (string.IsNullOrEmpty(appId)) throw new InvalidUrlException(Name);
        return $"https://apps.galaxyappstore.com/detail/{appId}";
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(standardUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var packageName = query["appId"];
        if (string.IsNullOrEmpty(packageName))
            packageName = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        var deviceId = GetString(additionalSettings, "deviceId");
        if (string.IsNullOrEmpty(deviceId)) deviceId = "SM-S948B";
        var csc = GetString(additionalSettings, "csc");
        if (string.IsNullOrEmpty(csc)) csc = "DBT";
        var isChina = csc.Trim().ToUpperInvariant() == "CHC";
        var mcc = GetString(additionalSettings, "mcc")?.Trim() ?? "";
        var mnc = GetString(additionalSettings, "mnc")?.Trim() ?? "";

        var device = new Dictionary<string, string>
        {
            ["deviceId"] = deviceId,
            ["csc"] = isChina ? "CHC" : csc,
            ["sdkVer"] = "37", // Windows mapping: upstream's no-device fallback.
            ["mcc"] = mcc.Length > 0 ? mcc : (isChina ? "460" : "425"),
            ["mnc"] = mnc.Length > 0 ? mnc : (isChina ? "00" : "01"),
        };
        try
        {
            return await GetStubDetailsAsync(packageName, device, cancellationToken);
        }
        catch (ObtainiumHttpException e) when (e.StatusCode == 429)
        {
            throw;
        }
        catch (Exception)
        {
            // Stub unavailable: fall back to the ODS protocol.
            return await GetOdsDetailsAsync(packageName, device, cancellationToken);
        }
    }
}
