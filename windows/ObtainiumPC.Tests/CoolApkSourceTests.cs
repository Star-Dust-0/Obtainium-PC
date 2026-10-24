using System.Net;
using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.CoolApk;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/coolapk.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: version/author/name/changelog
/// from /v6/apk/detail, releaseDate = lastupdate seconds -> ms, the APK URL is
/// the Location header of the no-redirect /v6/apk/download call, the asset is
/// named "{appId}_{version}.apk", and status -2 or null data is NoReleases.
/// </summary>
public class CoolApkSourceTests
{
    private const string StoreUrl = "https://www.coolapk.com/apk/com.example.app";
    private const string DetailUrl = "https://api2.coolapk.com/v6/apk/detail?id=com.example.app";
    private const string DownloadUrl = "https://api2.coolapk.com/v6/apk/download?pn=com.example.app&aid=12345";

    private const string Detail = """
        {
          "status": 1,
          "data": {
            "id": 12345,
            "apkversionname": "3.2.1",
            "title": "Cool App",
            "developername": "Some Dev",
            "changelog": "- fixed bugs",
            "lastupdate": 1777000000
          }
        }
        """;

    private static CoolApkSource Source(FixtureHttpHandler handler) => new(FixtureHttpHandler.Client(handler));

    private static FixtureHttpHandler BaseHandler() => new FixtureHttpHandler()
        .Respond(DetailUrl, Detail)
        .Respond(DownloadUrl, "", HttpStatusCode.Found,
            new Dictionary<string, string> { ["Location"] = "https://dl.coolapk.com/down/com.example.app-3.2.1.apk" });

    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    [Fact]
    public async Task Detail_fields_and_redirected_apk_url_resolve()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal("3.2.1", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Some Dev", details.Names.Author);
        Assert.Equal("- fixed bugs", details.ChangeLog);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1777000000), details.ReleaseDate);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("com.example.app_3.2.1.apk", asset.Key);
        Assert.Equal("https://dl.coolapk.com/down/com.example.app-3.2.1.apk", asset.Value);
    }

    [Fact]
    public async Task Status_minus_two_is_no_releases()
    {
        var handler = new FixtureHttpHandler()
            .Respond(DetailUrl, """{"status": -2, "data": null}""");
        await Assert.ThrowsAsync<NoReleasesException>(
            () => Source(handler).GetLatestAssetDetailsAsync(StoreUrl, NoSettings));
    }

    [Fact]
    public async Task Empty_download_location_is_no_asset()
    {
        var handler = new FixtureHttpHandler()
            .Respond(DetailUrl, Detail)
            .Respond(DownloadUrl, "", HttpStatusCode.OK);
        await Assert.ThrowsAsync<NoAppAssetException>(
            () => Source(handler).GetLatestAssetDetailsAsync(StoreUrl, NoSettings));
    }

    [Fact]
    public async Task Token_headers_accompany_requests()
    {
        var source = Source(BaseHandler());
        var headers = source.GetDownloadRequestHeaders(
            "https://dl.coolapk.com/down/com.example.app-3.2.1.apk", StoreUrl, NoSettings);
        Assert.NotNull(headers);
        Assert.Equal("com.coolapk.market", headers!["X-App-Id"]);
        Assert.StartsWith("v2", headers["X-App-Token"]);
        Assert.False(string.IsNullOrEmpty(headers["X-App-Device"]));
    }
}
