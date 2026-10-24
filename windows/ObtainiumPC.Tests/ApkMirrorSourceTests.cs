using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.ApkMirror;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/apkmirror.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: title cleaning and version
/// tokens, icon-url package extraction, file-size parsing, release-page
/// changelog block, RSS feed fallback (always track-only, empty assets).
/// </summary>
public class ApkMirrorSourceTests
{
    [Theory]
    [InlineData("App 42.5.15-21 by Some Dev (arm64-v8a) (Android 8.0+)", "42.5.15-21")]
    [InlineData("Cool App 1.2.3 by Dev", "1.2.3")]
    [InlineData("No Version Here by Dev", null)]
    public void Version_tokens_come_from_cleaned_titles(string title, string? expected) =>
        Assert.Equal(expected, ApkMirrorSource.VersionFromTitle(title));

    [Theory]
    [InlineData("https://www.apkmirror.com/uploads/65a_com.android.chrome-384x384.png", "com.android.chrome")]
    [InlineData("https://www.apkmirror.com/logo_apkmirror.png", null)]
    public void Package_names_come_from_icon_filenames(string url, string? expected) =>
        Assert.Equal(expected, ApkMirrorSource.PackageFromIconUrl(url));

    [Theory]
    [InlineData("File size: 270.70 MB", 283849523L)]
    [InlineData("File size: 2.5 KB", 2560L)]
    [InlineData("no size here", null)]
    public void File_sizes_parse_to_bytes(string text, long? expected) =>
        Assert.Equal(expected, ApkMirrorSource.SizeBytesFromPageText(text));

    [Fact]
    public void Changelog_block_stops_at_about_and_download_sections()
    {
        const string html = """
            <html><body>
            <h3>What's new in Cool App 1.2.3</h3>
            <ul><li>first fix</li><li>second fix</li></ul>
            <p>Advertisement</p>
            <h3>Download Cool App</h3>
            <p>should not appear</p>
            </body></html>
            """;
        // For a bare <ul> sibling, both Dart and this port take the plain-text branch per <li>.
        Assert.Equal("first fix\nsecond fix", ApkMirrorSource.ChangeLogFromReleasePageHtml(html));
    }

    [Fact]
    public async Task Feed_fallback_resolves_version_release_url_and_package()
    {
        const string storeUrl = "https://www.apkmirror.com/apk/cool-dev/cool-app";
        const string feed = """
            <rss><channel>
            <item><title>Cool App 1.2.3 by Cool Dev</title>
            <link>https://www.apkmirror.com/apk/cool-dev/cool-app/cool-app-1-2-3-release/</link>
            <pubDate>Sat, 10 Oct 2026 12:00:00 +0000</pubDate>
            <content:encoded><![CDATA[<img src="https://www.apkmirror.com/up/x_com.cool.app-384x384.png"/>]]></content:encoded>
            </item>
            </channel></rss>
            """;
        // The size line sits outside the changelog block (Dart's stop list
        // would not cut it); page text is scanned whole for the size.
        const string releasePage = """
            <html><body>
            <p>File size: 10.0 MB</p>
            <h3>What's new in Cool App 1.2.3</h3>
            <p>cool fixes</p>
            </body></html>
            """;
        var handler = new FixtureHttpHandler()
            .Respond($"{storeUrl}/feed/", feed)
            .Respond("https://www.apkmirror.com/apk/cool-dev/cool-app/cool-app-1-2-3-release/", releasePage);
        var details = await new ApkMirrorSource(FixtureHttpHandler.Client(handler))
            .GetLatestAssetDetailsAsync(storeUrl, new Dictionary<string, JsonElement>());
        Assert.Equal("1.2.3", details.Version);
        Assert.Empty(details.AssetUrls); // APKMirror forbids direct downloads: track-only.
// getAppNames uses the raw URL path segments (apk/<author>/<name>).
        Assert.Equal("cool-dev", details.Names.Author);
        Assert.Equal("cool-app", details.Names.Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), details.ReleaseDate);
        Assert.Equal("https://www.apkmirror.com/apk/cool-dev/cool-app/cool-app-1-2-3-release/", details.ReleaseUrl);
        Assert.Equal("com.cool.app", details.AppId);
        Assert.Equal("cool fixes", details.ChangeLog);
        Assert.Equal(10485760, details.ApkSizeBytes);
    }
}
