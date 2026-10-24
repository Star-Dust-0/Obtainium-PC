using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.HuaweiAppGallery;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/huaweiappgallery.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: front2 handshake with zone-based
/// host selection (EU default, CN/RU/DR2), signed appDetailById and
/// searchApp, asset "{package ?? cId}.apk", API failure fails the check.
/// </summary>
public class HuaweiAppGallerySourceTests : IDisposable
{
    private const string StoreUrl = "https://appgallery.huawei.com/app/C1000";
    private const string EuApi = "https://store-dre.hispace.dbankcloud.com/hwmarket/api/clientApi";
    private const string CnApi = "https://store-drcn.hispace.dbankcloud.com/hwmarket/api/clientApi";

    private const string EuProbe = """{"serviceZone":"DE","sign":"SIG"}""";
    private const string Detail = """
        {"rtnCode":0,"detailInfo":[{"versionName":"1.2.3","package":"com.cool.app",
        "name":"Cool App","developer":"Cool Dev","url":"https://dbankcloud.com/app.apk",
        "releaseDate":"2026-10-01"}]}
        """;

    public HuaweiAppGallerySourceTests() => HuaweiAppGallerySource.ResetSession();
    public void Dispose() => HuaweiAppGallerySource.ResetSession();

    [Fact]
    public async Task Latest_details_come_from_the_signed_store_api()
    {
        var handler = new FixtureHttpHandler().RespondSequence(EuApi, EuProbe, Detail);
        var details = await new HuaweiAppGallerySource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("1.2.3", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Cool Dev", details.Names.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), details.ReleaseDate);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("com.cool.app.apk", asset.Key);
        Assert.Equal("https://dbankcloud.com/app.apk", asset.Value);
    }

    [Fact]
    public async Task Chinese_zone_selects_the_chinese_host()
    {
        var handler = new FixtureHttpHandler()
            .Respond(EuApi, """{"serviceZone":"CN"}""")
            .Respond(CnApi, """{"sign":"SIGCN"}""")
            .RespondSequence(CnApi, """{"sign":"SIGCN"}""", Detail);
        // Probe to EU returns no sign and zone CN; refresh front2 hits CN, then detail hits CN.
        var details = await new HuaweiAppGallerySource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("1.2.3", details.Version);
    }

    [Fact]
    public async Task Api_failure_fails_the_check()
    {
        // detailInfo empty on both attempts (second attempt refreshes via front2).
        var handler = new FixtureHttpHandler().RespondSequence(EuApi,
            EuProbe,
            """{"rtnCode":7,"rtnDesc":"expired"}""",
            EuProbe,
            """{"rtnCode":7,"rtnDesc":"expired"}""");
        await Assert.ThrowsAsync<ObtainiumException>(
            () => new HuaweiAppGallerySource(FixtureHttpHandler.Client(handler))
                .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public async Task Search_flattens_layout_data()
    {
        const string search = """
            {"rtnCode":0,"layoutData":[{"dataList":[
                {"appInfo":{"appid":"C1000","name":"Cool App","package":"com.cool.app"}},
                {"appid":"C2000","name":"Other App"}
            ]}]}
            """;
        var handler = new FixtureHttpHandler().RespondSequence(EuApi, EuProbe, search);
        var results = await new HuaweiAppGallerySource(FixtureHttpHandler.Client(handler))
            .SearchAsync("cool", new Dictionary<string, JsonElement>());
        Assert.Equal(2, results!.Count);
        Assert.Equal(new[] { "Cool App", "com.cool.app" }, results["https://appgallery.huawei.com/app/C1000"]);
        Assert.Equal(new[] { "Other App" }, results["https://appgallery.huawei.com/app/C2000"]);
    }
}
