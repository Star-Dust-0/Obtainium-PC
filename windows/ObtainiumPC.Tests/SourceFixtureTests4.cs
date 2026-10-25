using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.Apk4Free;
using ObtainiumPC.Sources.Aptoide;
using ObtainiumPC.Sources.Farsroid;
using ObtainiumPC.Sources.LiteApks;
using ObtainiumPC.Sources.RockMods;
using ObtainiumPC.Sources.TelegramApp;
using ObtainiumPC.Sources.Tencent;
using Xunit;

namespace ObtainiumPC.Tests;

/// Hermetic fixtures for the track-only store scrapers (apk4free.dart,
/// aptoide.dart, farsroid.dart, liteapks.dart, rockmods.dart,
/// telegramapp.dart, tencent.dart). They prove parsing against recorded-shape
/// responses, not that the live sites still serve that shape.
public class TrackOnlyStoreFixtureTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> None = new Dictionary<string, JsonElement>();

    private static IReadOnlyDictionary<string, JsonElement> Settings(params (string, object)[] kv) =>
        kv.ToDictionary(x => x.Item1, x => JsonSerializer.SerializeToElement(x.Item2));

    private static HttpClient Http(FixtureHttpHandler h) => FixtureHttpHandler.Client(h);

    // ---- Apk4Free ------------------------------------------------------

    [Fact]
    public async Task Apk4Free_two_page_scrape_cleans_title_and_reads_version()
    {
        var store = "https://apk4free.net/cool-app";
        var dl = "https://apk4free.net/cool-app/download/";
        var h = new FixtureHttpHandler()
            .Respond(store, """
                <html><h1 class="main-box-title">Cool App MOD APK (Premium Unlocked) v2.3.4</h1>
                <div class="version">2.3.4</div>
                <a class="downloadAPK" href="https://apk4free.net/cool-app/download/">Download</a></html>
                """)
            .Respond(dl, """<html><a class="downloadAPK" href="https://cdn.apk4free.net/cool-app-2.3.4.apk">Cool App 2.3.4</a></html>""");
        var d = await new Apk4FreeSource(Http(h)).GetLatestAssetDetailsAsync(store, None);
        Assert.Equal("2.3.4", d.Version);
        Assert.Equal("Cool App", d.Names.Name);
        Assert.Equal("https://cdn.apk4free.net/cool-app-2.3.4.apk", Assert.Single(d.AssetUrls).Value);
    }

    [Fact]
    public async Task Apk4Free_without_download_link_is_no_releases_and_without_apks_no_asset()
    {
        var store = "https://apk4free.net/cool-app";
        var none = new FixtureHttpHandler().Respond(store, "<html><h1 class=\"main-box-title\">X</h1></html>");
        await Assert.ThrowsAsync<NoReleasesException>(() => new Apk4FreeSource(Http(none)).GetLatestAssetDetailsAsync(store, None));
        var emptyDl = new FixtureHttpHandler()
            .Respond(store, "<html><div class=\"version\">1.0</div><a href=\"https://apk4free.net/cool-app/download/\">d</a></html>")
            .Respond("https://apk4free.net/cool-app/download/", "<html></html>");
        await Assert.ThrowsAsync<NoAppAssetException>(() => new Apk4FreeSource(Http(emptyDl)).GetLatestAssetDetailsAsync(store, None));
    }

    // ---- Aptoide -------------------------------------------------------

    [Fact]
    public async Task Aptoide_reads_app_id_then_api_details()
    {
        var store = "https://cool-app.en.aptoide.com";
        var h = new FixtureHttpHandler()
            .Respond(store, """<script>{"app": {"id": 4242, "x": 1}}</script>""")
            .Respond("https://ws2.aptoide.com/api/7/getApp/app_id/4242", """
                { "nodes": { "meta": { "data": { "name": "Cool App", "developer": { "name": "Acme" },
                  "updated": "2026-10-01 10:00:00",
                  "file": { "vername": "5.6.7", "path": "https://pool.apk.aptoide.com/cool/cool-5.6.7.apk" } } } } }
                """);
        var d = await new AptoideSource(Http(h)).GetLatestAssetDetailsAsync(store, None);
        Assert.Equal("5.6.7", d.Version);
        Assert.Equal("Acme", d.Names.Author);
        Assert.Equal("cool-5.6.7.apk", Assert.Single(d.AssetUrls).Key);
        Assert.NotNull(d.ReleaseDate);
    }

    [Fact]
    public async Task Aptoide_missing_id_or_version_surface_distinct_errors()
    {
        var store = "https://cool-app.en.aptoide.com";
        await Assert.ThrowsAsync<NoReleasesException>(() =>
            new AptoideSource(Http(new FixtureHttpHandler().Respond(store, "<html>no app here</html>"))).GetLatestAssetDetailsAsync(store, None));
        var noVer = new FixtureHttpHandler()
            .Respond(store, """{"app":{"id":1}}""")
            .Respond("https://ws2.aptoide.com/api/7/getApp/app_id/1",
                """{ "nodes": { "meta": { "data": { "name": "X", "file": { "path": "https://p/x.apk" } } } } }""");
        await Assert.ThrowsAsync<ObtainiumPC.Sources.FDroid.NoVersionException>(() =>
            new AptoideSource(Http(noVer)).GetLatestAssetDetailsAsync(store, None));
    }

    // ---- Farsroid ------------------------------------------------------

    [Fact]
    public async Task Farsroid_uses_download_box_api_and_first_apk()
    {
        var page = "https://farsroid.com/cool-app";
        var h = new FixtureHttpHandler()
            .Respond(page, """<html><div class="download-links" data-post-id="77" data-post-version="3.2.1"></div></html>""")
            .Respond("https://farsroid.com/api/download-box/?post_id=77&post_version=3.2.1", """
                { "data": { "content": "<a href=\"https://dl.farsroid.com/cool-3.2.1-arm64.apk\">arm64</a><a href=\"https://dl.farsroid.com/cool-3.2.1-arm.apk\">arm</a>" } }
                """);
        var d = await new FarsroidSource(Http(h)).GetLatestAssetDetailsAsync(page, Settings(("useFirstApkOfVersion", true)));
        Assert.Equal("3.2.1", d.Version);
        Assert.Equal("cool-3.2.1-arm64.apk", Assert.Single(d.AssetUrls).Key);
        var all = await new FarsroidSource(Http(h)).GetLatestAssetDetailsAsync(page, None);
        Assert.Equal(2, all.AssetUrls.Count);
    }

    [Fact]
    public async Task Farsroid_without_download_box_is_no_releases()
    {
        var page = "https://farsroid.com/cool-app";
        await Assert.ThrowsAsync<NoReleasesException>(() =>
            new FarsroidSource(Http(new FixtureHttpHandler().Respond(page, "<html></html>"))).GetLatestAssetDetailsAsync(page, None));
    }

    // ---- LiteAPKs ------------------------------------------------------

    [Fact]
    public async Task LiteApks_wp_json_posts_to_version_and_download_links_with_referer()
    {
        var page = "https://liteapks.com/cool-app.123";
        var h = new FixtureHttpHandler()
            .Respond("https://liteapks.com/wp-json/wp/v2/posts?slug=cool-app", """[ { "id": 123 } ]""")
            .Respond("https://liteapks.com/wp-json/v2/posts/123", """
                { "data": { "title": "Cool App", "publisher": "Acme",
                  "versions": [ { "version": "9.8.7", "version_downloads": [
                    { "version_download_link": "https://cdn.liteapks.com/files/Cool%20App-9.8.7.apk" } ] } ] } }
                """);
        var src = new LiteApksSource(Http(h));
        var d = await src.GetLatestAssetDetailsAsync(page, None);
        Assert.Equal("9.8.7", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("Cool App-9.8.7.apk", a.Key);
        Assert.EndsWith("#" + page, a.Value);
        Assert.Equal(page, src.GetDownloadRequestHeaders(a.Value, page, None)!["Referer"]);
        Assert.Contains("?token=", src.AssetUrlPrefetchModifier(a.Value, page, None));
    }

    [Fact]
    public async Task LiteApks_unknown_slug_is_no_releases()
    {
        var page = "https://liteapks.com/missing.1";
        var h = new FixtureHttpHandler().Respond("https://liteapks.com/wp-json/wp/v2/posts?slug=missing", "[]");
        await Assert.ThrowsAsync<NoReleasesException>(() => new LiteApksSource(Http(h)).GetLatestAssetDetailsAsync(page, None));
    }

    // ---- RockMods ------------------------------------------------------

    [Fact]
    public async Task RockMods_reads_version_from_json_ld_and_has_no_assets()
    {
        var page = "https://rockmods.net/apps/cool-app";
        var h = new FixtureHttpHandler().Respond(page, """
            <html><script type="application/ld+json">{ "@type": "SoftwareApplication", "name": "Cool App",
              "softwareVersion": "1.9.0", "author": { "name": "Acme" } }</script></html>
            """);
        var d = await new RockModsSource(Http(h)).GetLatestAssetDetailsAsync(page, None);
        Assert.Equal("1.9.0", d.Version);
        Assert.Equal("Acme", d.Names.Author);
        Assert.Empty(d.AssetUrls);
    }

    [Fact]
    public async Task RockMods_page_without_json_ld_version_is_no_version()
    {
        var page = "https://rockmods.net/apps/cool-app";
        var h = new FixtureHttpHandler().Respond(page, "<html><h1>Cool App</h1></html>");
        await Assert.ThrowsAsync<ObtainiumPC.Sources.FDroid.NoVersionException>(() =>
            new RockModsSource(Http(h)).GetLatestAssetDetailsAsync(page, None));
    }

    // ---- Telegram App --------------------------------------------------

    [Fact]
    public async Task TelegramApp_takes_version_from_last_channel_message()
    {
        var h = new FixtureHttpHandler().Respond("https://t.me/s/TAndroidAPK", """
            <html><div class="tgme_widget_message_text">11.0.0 (5000)<br/>older</div>
            <div class="tgme_widget_message_text">11.1.2 (5100)<br/>newer</div></html>
            """);
        var src = new TelegramAppSource(Http(h));
        var d = await src.GetLatestAssetDetailsAsync("https://telegram.org", None);
        Assert.Equal("11.1.2", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("telegram-11.1.2.apk", a.Key);
        Assert.Equal("https://telegram.org/dl/android/apk", a.Value);
        Assert.Equal("https://telegram.org", src.SourceSpecificStandardizeUrl("https://t.me/anything"));
    }

    [Fact]
    public async Task TelegramApp_channel_without_messages_is_no_version()
    {
        var h = new FixtureHttpHandler().Respond("https://t.me/s/TAndroidAPK", "<html></html>");
        await Assert.ThrowsAsync<ObtainiumPC.Sources.FDroid.NoVersionException>(() =>
            new TelegramAppSource(Http(h)).GetLatestAssetDetailsAsync("https://telegram.org", None));
    }

    // ---- Tencent -------------------------------------------------------

    [Fact]
    public async Task Tencent_parses_window_systemData_and_prefers_64bit_apk()
    {
        var page = "https://sj.qq.com/appdetail/com.cool.app";
        var body = "<html><script>\nwindow.systemData={\"appDetail\":{\"versionName\":\"6.5.4\",\"appName\":\"Cool\"," +
                   "\"author\":\"Acme\",\"apkUrl\":\"https://dd.myapp.com/a32.apk?fsname=com.cool.app_6.5.4_32.apk\"," +
                   "\"apkUrl64\":\"https://dd.myapp.com/a64.apk?fsname=com.cool.app_6.5.4_64.apk\"}}\n</script></html>";
        var h = new FixtureHttpHandler().Respond("https://a.app.qq.com/o/simple.jsp?pkgname=com.cool.app", body);
        var d = await new TencentSource(Http(h)).GetLatestAssetDetailsAsync(page, None);
        Assert.Equal("6.5.4", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("com.cool.app_6.5.4_64.apk", a.Key);
        Assert.Contains("a64.apk", a.Value);
    }

    [Fact]
    public async Task Tencent_missing_system_data_is_no_releases()
    {
        var page = "https://sj.qq.com/appdetail/com.cool.app";
        var h = new FixtureHttpHandler().Respond("https://a.app.qq.com/o/simple.jsp?pkgname=com.cool.app", "<html></html>");
        await Assert.ThrowsAsync<NoReleasesException>(() => new TencentSource(Http(h)).GetLatestAssetDetailsAsync(page, None));
    }
}
