using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.ItchIo;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/itchio.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: title/author parsing, version
/// fallback chain (page version, then "Updated" date, then 'latest'), and the
/// platform-icon filter (Windows mapping: icon-windows8).
/// </summary>
public class ItchIoSourceTests
{
    private const string StoreUrl = "https://author.itch.io/game";

    // Upload divs directly on the store page (no paywall step needed).
    private const string StorePage = """
        <html><head><title>Cool Game by Some Author</title></head>
        <body>
          <span class="on_follow"><span class="full_label">Follow Some Author</span></span>
          <abbr title="15 March 2026 @ 10:00 UTC">updated</abbr>
          <div class="upload">
            <div class="upload_name"><strong class="name" title="Cool Game Windows">Cool Game Windows</strong></div>
            <span class="download_platforms"><span class="icon-windows8"></span></span>
            <a class="download_btn" data-upload_id="111">Download</a>
          </div>
          <div class="upload">
            <div class="upload_name"><strong class="name" title="Cool Game Android">Cool Game Android</strong></div>
            <span class="download_platforms"><span class="icon-android"></span></span>
            <a class="download_btn" data-upload_id="222">Download</a>
          </div>
        </body></html>
        """;

    private static ItchIoSource Source(FixtureHttpHandler handler) => new(FixtureHttpHandler.Client(handler));
    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    private static FixtureHttpHandler BaseHandler() => new FixtureHttpHandler()
        .Respond(StoreUrl, StorePage)
        .Respond("https://author.itch.io/game/file/111?as_props=1&source=game_download", """{"url":"https://cdn.example/signed-file"}""")
        .Respond("https://cdn.example/signed-file", "binary");

    [Fact]
    public async Task Title_and_author_come_from_page()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal("Cool Game", details.Names.Name);
        Assert.Equal("Some Author", details.Names.Author);
    }

    [Fact]
    public async Task Only_windows_uploads_are_selected()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Single(details.AssetUrls);
        Assert.Equal("https://author.itch.io/game/download/111", details.AssetUrls[0].Value);
    }

    [Fact]
    public async Task Version_falls_back_to_update_date_then_latest()
    {
        // No parseable version on the page -> YYYYMMDD from the abbr title.
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal("20260315", details.Version);

        // No abbr either -> 'latest'.
        var noDate = StorePage.Replace("""<abbr title="15 March 2026 @ 10:00 UTC">updated</abbr>""", "");
        var handler = new FixtureHttpHandler()
            .Respond(StoreUrl, noDate)
            .Respond("https://author.itch.io/game/file/111?as_props=1&source=game_download", """{"url":"https://cdn.example/signed-file"}""")
            .Respond("https://cdn.example/signed-file", "binary");
        var details2 = await Source(handler).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal("latest", details2.Version);
    }

    [Fact]
    public async Task No_windows_uploads_throws_no_app_asset()
    {
        var androidOnly = StorePage.Replace("icon-windows8", "icon-apple");
        var handler = new FixtureHttpHandler().Respond(StoreUrl, androidOnly);
        await Assert.ThrowsAsync<NoAppAssetException>(() =>
            Source(handler).GetLatestAssetDetailsAsync(StoreUrl, NoSettings));
    }

    [Fact]
    public async Task Prefetch_modifier_resolves_signed_url_at_download_time()
    {
        var source = Source(new FixtureHttpHandler()
            .Respond("https://author.itch.io/game", StorePage)
            .Respond("https://author.itch.io/game/file/111?as_props=1&source=game_download", """{"url":"https://cdn.example/fresh-signed"}"""));
        var resolved = await source.AssetUrlPrefetchModifierAsync(
            "https://author.itch.io/game/download/111", StoreUrl, NoSettings);
        Assert.Equal("https://cdn.example/fresh-signed", resolved);
    }
}
