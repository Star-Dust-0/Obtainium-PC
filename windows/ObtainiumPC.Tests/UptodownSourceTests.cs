using System.Text;
using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.Uptodown;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/uptodown.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: page scraping (div.version,
/// #detail-app-name, #author-link, #technical-information with icon-fragment
/// fallback), asset "{appId}.{ext}" at "{standard}/{fileId}-x", eAPI session
/// via HMAC-signed auth, download URL resolution, Android-only search results.
/// </summary>
public class UptodownSourceTests
{
    private const string StoreUrl = "https://cool-app.en.uptodown.com/android/download";

    private const string Page = """
        <html><body>
        <div id="detail-app-name" data-file-id="12345" data-code="com.cool.app">Cool App</div>
        <div class="version">4.2.0</div>
        <a id="author-link">Cool Dev</a>
        <table id="technical-information">
          <tr><th>Package Name</th><td>com.cool.app</td></tr>
          <tr><th>Date</th><td>Oct 15, 2026</td></tr>
          <tr><th>File type</th><td>APK</td></tr>
        </table>
        <button id="detail-download-button" data-app-id="com.cool.app" data-file-id="12345"></button>
        </body></html>
        """;

    private static string FakeJwt(long exp)
    {
        string B64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("{\"alg\":\"HS256\"}")}.{B64Url($"{{\"exp\":{exp}}}")}.{B64Url("sig")}";
    }

    private static readonly string AuthResponse =
        $"{{\"token\":\"{FakeJwt(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600)}\"}}";

    [Fact]
    public async Task Latest_details_scrape_the_download_page()
    {
        var handler = new FixtureHttpHandler().Respond(StoreUrl, Page);
        var details = await new UptodownSource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("4.2.0", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Cool Dev", details.Names.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), details.ReleaseDate);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("com.cool.app.apk", asset.Key);
        Assert.Equal(StoreUrl + "/12345-x", asset.Value);
    }

    [Fact]
    public async Task Technical_info_falls_back_to_icon_fragments()
    {
        // Labels localized: values recovered from the icon fragment per upstream.
        const string page = """
            <html><body>
            <div id="detail-app-name" data-file-id="999">App</div>
            <div class="version">1.0</div>
            <table id="technical-information">
              <tr><th>Nombre del paquete</th><td><img src="/img/i.svg#icon-40-package"/></td><td>com.icon.app</td></tr>
              <tr><th>Tipo de archivo</th><td><img src="/img/i.svg#icon-40-type"/></td><td>XAPK</td></tr>
            </table>
            </body></html>
            """;
        var handler = new FixtureHttpHandler().Respond(StoreUrl, page);
        var details = await new UptodownSource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("com.icon.app.xapk", Assert.Single(details.AssetUrls).Key);
    }

    [Fact]
    public async Task Prefetch_modifier_resolves_download_through_eapi_session()
    {
        var handler = new FixtureHttpHandler()
            .Respond(StoreUrl + "/12345-x", Page)
            .RespondPrefix("https://www.uptodown.app/eapi/auth/token", AuthResponse)
            .Respond("https://www.uptodown.app/eapi/apps/com.cool.app/file/12345/downloadUrl",
                """{"success":1,"data":{"downloadURL":"https://dw.uptodown.com/dwn/cool.apk"}}""");
        var resolved = await new UptodownSource(FixtureHttpHandler.Client(handler))
            .AssetUrlPrefetchModifierAsync(StoreUrl + "/12345-x", StoreUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("https://dw.uptodown.com/dwn/cool.apk", resolved);
    }

    [Fact]
    public async Task Search_keeps_android_apps_and_strips_markup()
    {
        var handler = new FixtureHttpHandler().Respond(
            "https://en.uptodown.com/android/en/s",
            """{"success":1,"data":{"apps":[
                {"url":"https://cool.en.uptodown.com/android","name":"<b>Cool</b> App","author":"Cool Dev","platformURL":"/android"},
                {"url":"https://cool.en.uptodown.com/mac","name":"Cool Mac","author":"Cool Dev","platformURL":"/mac"}
            ]}}""");
        var results = await new UptodownSource(FixtureHttpHandler.Client(handler))
            .SearchAsync("cool", new Dictionary<string, JsonElement>());
        var hit = Assert.Single(results!);
        Assert.Equal("https://cool.en.uptodown.com/android", hit.Key);
        Assert.Equal(new[] { "Cool App", "Cool Dev" }, hit.Value);
    }
}
