using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.ApkCombo;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/apkcombo.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: one current build per variant,
/// /r2 redirector unwrapping, version/app_name/author selectors, and
/// download-time re-scrape matching by stable R2 object path.
/// </summary>
public class ApkComboSourceTests
{
    private const string StoreUrl = "https://apkcombo.org.downloader.world.apkcombo.com/x/app.id";
    private const string DownloadUrl = StoreUrl + "/download/apk";

    private const string StorePage = """
        <html><body>
          <div class="app_name">Cool App</div>
          <div class="author">Some Dev</div>
          <div class="version">4.5.6</div>
          <div class="information-table">
            <div class="item"><div class="value">Tools</div></div>
            <div class="item"><div class="value">Mar 3, 2026</div></div>
          </div>
        </body></html>
        """;

    private const string DownloadPage = """
        <html><body>
          <div id="variants-tab"><div><ul>
            <li>
              <code>arm64-v8a:</code>
              <a href="/r2?u=https%3A%2F%2Fr2.example%2Fobj%2Fapp-456-arm64.apk%3FX-Amz-Expires%3D60">
                <span class="info"><span class="header"><span class="vercode">456</span></span></span>
              </a>
              <a href="/r2?u=https%3A%2F%2Fr2.example%2Fobj%2Fapp-400-arm64.apk%3FX-Amz-Expires%3D60">
                <span class="info"><span class="header"><span class="vercode">400</span></span></span>
              </a>
            </li>
            <li>
              <code>armeabi-v7a:</code>
              <a href="https://r2.example/obj/app-456-armv7.apk?X-Amz-Expires=60">
                <span class="info"><span class="header"><span class="vercode">456</span></span></span>
              </a>
            </li>
          </ul></div></div>
        </body></html>
        """;

    private static ApkComboSource Source(FixtureHttpHandler handler) => new(FixtureHttpHandler.Client(handler));
    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    private static FixtureHttpHandler BaseHandler() => new FixtureHttpHandler()
        .Respond(StoreUrl, StorePage)
        .Respond(DownloadUrl, DownloadPage);

    [Fact]
    public async Task Version_name_author_and_date_from_store_page()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal("4.5.6", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Some Dev", details.Names.Author);
        Assert.NotNull(details.ReleaseDate);
    }

    [Fact]
    public async Task Only_the_first_current_build_per_variant_is_listed()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Equal(2, details.AssetUrls.Count);
        Assert.DoesNotContain(details.AssetUrls, a => a.Value.Contains("app-400"));
    }

    [Fact]
    public async Task R2_redirector_links_are_unwrapped()
    {
        var details = await Source(BaseHandler()).GetLatestAssetDetailsAsync(StoreUrl, NoSettings);
        Assert.Contains(details.AssetUrls,
            a => a.Value.StartsWith("https://r2.example/obj/app-456-arm64.apk"));
    }

    [Fact]
    public async Task Prefetch_modifier_matches_fresh_link_by_object_path()
    {
        var source = Source(BaseHandler());
        var resolved = await source.AssetUrlPrefetchModifierAsync(
            "https://r2.example/obj/app-456-armv7.apk?X-Amz-Expires=1", StoreUrl, NoSettings);
        Assert.Equal("https://r2.example/obj/app-456-armv7.apk?X-Amz-Expires=60", resolved);
    }

    [Fact]
    public async Task Missing_stored_asset_at_download_time_throws()
    {
        var source = Source(BaseHandler());
        await Assert.ThrowsAsync<NoAppAssetException>(() => source.AssetUrlPrefetchModifierAsync(
            "https://r2.example/obj/removed.apk", StoreUrl, NoSettings));
    }
}
