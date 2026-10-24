using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.RuStore;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/rustore.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: overallInfo + download-link API,
/// .zip-to-.apk single-URL mapping, base-plus-splits joined with '\n',
/// aggregated cards rejected, Android search mapping.
/// </summary>
public class RuStoreSourceTests
{
    private const string StoreUrl = "https://www.rustore.ru/catalog/app/com.cool.app";
    private const string AppInfoUrl = "https://backapi.rustore.ru/applicationData/overallInfo/com.cool.app";
    private const string DownloadLinkUrl = "https://backapi.rustore.ru/v3/showcase/apps/download-link";
    private const string NonceUrl = "https://api.rustore.ru/v1/secure/nonce";
    private const string NonceResponse = """{"nonce":"AAECAwQFBgc="}""";

    private const string AppInfo = """
        {"body":{"appId":123,"appName":"Cool App","companyName":"Cool Dev",
        "appVerUpdatedAt":"2026-10-01T12:00:00","versionName":"5.1","whatsNew":"fixes"}}
        """;

    private static RuStoreSource Source(FixtureHttpHandler handler) =>
        new(FixtureHttpHandler.Client(handler.RespondPrefix(NonceUrl, NonceResponse)));

    [Fact]
    public async Task Single_download_url_maps_zip_to_apk()
    {
        var handler = new FixtureHttpHandler()
            .Respond(AppInfoUrl, AppInfo)
            .Respond(DownloadLinkUrl, """{"downloadUrls":[{"url":"https://dl.rustore.ru/apps/app.zip"}]}""");
        var details = await Source(handler).GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("5.1", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Cool Dev", details.Names.Author);
        Assert.Equal("fixes", details.ChangeLog);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), details.ReleaseDate);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("app.apk", asset.Key);
        Assert.Equal("https://dl.rustore.ru/apps/app.apk", asset.Value);
    }

    [Fact]
    public async Task Multiple_download_urls_join_as_split_set()
    {
        var handler = new FixtureHttpHandler()
            .Respond(AppInfoUrl, AppInfo)
            .Respond(DownloadLinkUrl, """{"downloadUrls":[
                {"url":"https://dl.rustore.ru/x/base.apk"},
                {"url":"https://dl.rustore.ru/x/config.arm64.apk"}]}""");
        var details = await Source(handler).GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("base.apk", asset.Key);
        Assert.Equal("https://dl.rustore.ru/x/base.apk\nhttps://dl.rustore.ru/x/config.arm64.apk", asset.Value);
    }

    [Fact]
    public async Task Aggregated_card_is_rejected()
    {
        var handler = new FixtureHttpHandler()
            .Respond(AppInfoUrl, """{"body":{"appId":1,"versionName":"1.0","aggregatorInfo":{"source":"ext"}}}""")
            .Respond(DownloadLinkUrl, """{"downloadUrls":[]}""");
        var ex = await Assert.ThrowsAsync<ObtainiumException>(
            () => Source(handler).GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>()));
        Assert.Contains("RUSTORE_AGGREGATED_APP", ex.Message);
    }

    [Fact]
    public async Task Search_maps_content_to_catalog_urls()
    {
        var handler = new FixtureHttpHandler().Respond(
            "https://backapi.rustore.ru/applicationData/apps?query=cool&pageNumber=0&pageSize=20",
            """{"body":{"content":[{"packageName":"com.cool.app","appName":"Cool App"}]}}""");
        var results = await Source(handler).SearchAsync("cool", new Dictionary<string, JsonElement>());
        var hit = Assert.Single(results!);
        Assert.Equal("https://rustore.ru/catalog/app/com.cool.app", hit.Key);
        Assert.Equal(new[] { "Cool App", "com.cool.app" }, hit.Value);
    }
}
