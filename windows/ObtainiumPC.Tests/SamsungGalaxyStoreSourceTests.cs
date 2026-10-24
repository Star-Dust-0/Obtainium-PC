using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.SamsungGalaxyStore;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/samsunggalaxystore.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: VAS stub first (strict field
/// validation, secure download URI), ODS fallback with endpoint discovery,
/// grant echo binding, store-details enrichment via main/overview/main.
/// </summary>
public class SamsungGalaxyStoreSourceTests
{
    private const string StoreUrl = "https://apps.galaxyappstore.com/detail/com.cool.app";
    private const string VasUrl =
        "https://vas.samsungapps.com/stub/stubDownload.as?appId=com.cool.app&deviceId=SM-S948B&csc=DBT&sdkVer=37&mcc=425&mnc=01&systemId=1608665720954&abiType=64&extuk=0191d6627f38685f";
    private const string UsOds = "https://us-odc.samsungapps.com/ods.as";

    private static string Ods(string requestId, string values) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <SamsungProtocol><response id="{requestId}" returnCode="0">
          <errorInfo><errorString errorCode="0">success</errorString></errorInfo>
          <list>{values}</list>
        </response></SamsungProtocol>
        """;

    private static string Value(string name, string text) => $"""<value name="{name}">{text}</value>""";

    [Fact]
    public async Task Stub_success_is_enriched_with_store_details()
    {
        // Discovery hub 404s -> fallback us-odc; main/overview/main agree -> date + changelog.
        var handler = new FixtureHttpHandler()
            .Respond(VasUrl, """
                <?xml version="1.0"?>
                <result><resultCode>1</resultCode><appId>com.cool.app</appId>
                <productId>000012345678</productId><productName>Cool App</productName>
                <versionName>2.1.0</versionName><versionCode>210</versionCode>
                <contentSize>5000000</contentSize>
                <downloadURI>https://dl.samsungapps.com/cool.apk</downloadURI></result>
                """)
            .RespondSequence($"{UsOds}?reqId=2290&ot=01&ct=B",
                Ods("2290", Value("GUID", "com.cool.app") + Value("productID", "000012345678")
                    + Value("version", "2.1.0") + Value("versionCode", "210") + Value("realContentsSize", "5000000")),
                Ods("2290", Value("GUID", "com.cool.app") + Value("productID", "000012345678")
                    + Value("version", "2.1.0") + Value("versionCode", "210") + Value("realContentsSize", "5000000")))
            .Respond($"{UsOds}?reqId=2291&ot=01&ct=B",
                Ods("2291", Value("version", "2.1.0") + Value("realContentsSize", "5000000")
                    + Value("lastUpdateDate", "2026;10;05;") + Value("updateDescription", "cool fixes")));
        var details = await new SamsungGalaxyStoreSource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("2.1.0", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Samsung Galaxy Store", details.Names.Author);
        Assert.Equal("cool fixes", details.ChangeLog);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), details.ReleaseDate);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("com.cool.app.apk", asset.Key);
        Assert.Equal("https://dl.samsungapps.com/cool.apk", asset.Value);
    }

    [Fact]
    public async Task Stub_failure_falls_back_to_ods_download_info()
    {
        var handler = new FixtureHttpHandler()
            .Respond(VasUrl, """<?xml version="1.0"?><result><resultCode>0</resultCode></result>""")
            .Respond($"{UsOds}?reqId=2298&ot=01&ct=B",
                Ods("2298", Value("GUID", "com.cool.app") + Value("productID", "000012345678")
                    + Value("version", "2.1.0") + Value("versionCode", "210")
                    + Value("needToLogin", "0") + Value("installableYN", "Y")
                    + Value("productName", "Cool App")))
            .Respond($"{UsOds}?reqId=2311&ot=01&ct=B",
                Ods("2311", Value("productID", "000012345678") + Value("version", "2.1.0")
                    + Value("versionCode", "210") + Value("contentsSize", "5000000")
                    + Value("downLoadURI", "https://dl.samsungapps.com/cool.apk")));
        var details = await new SamsungGalaxyStoreSource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("2.1.0", details.Version);
        Assert.Equal("https://dl.samsungapps.com/cool.apk", Assert.Single(details.AssetUrls).Value);
    }

    [Fact]
    public async Task Login_required_metadata_is_an_error()
    {
        var handler = new FixtureHttpHandler()
            .Respond(VasUrl, """<?xml version="1.0"?><result><resultCode>0</resultCode></result>""")
            .Respond($"{UsOds}?reqId=2298&ot=01&ct=B",
                Ods("2298", Value("GUID", "com.cool.app") + Value("productID", "000012345678")
                    + Value("version", "2.1.0") + Value("versionCode", "210")
                    + Value("needToLogin", "1") + Value("installableYN", "Y")));
        var ex = await Assert.ThrowsAsync<ObtainiumException>(
            () => new SamsungGalaxyStoreSource(FixtureHttpHandler.Client(handler))
                .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>()));
        Assert.Contains("login", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standardize_uses_appId_query_or_last_path_segment()
    {
        var source = new SamsungGalaxyStoreSource();
        Assert.Equal("https://apps.galaxyappstore.com/detail/com.query.app",
            source.SourceSpecificStandardizeUrl("https://galaxystore.samsung.com/prepost/0001?appId=com.query.app"));
        Assert.Equal("https://apps.galaxyappstore.com/detail/com.path.app",
            source.SourceSpecificStandardizeUrl("https://apps.samsung.com/app/detail/com.path.app"));
        Assert.Throws<InvalidUrlException>(() => source.SourceSpecificStandardizeUrl("https://example.com/x"));
    }
}
