using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.DirectLink;
using ObtainiumPC.Sources.Jenkins;
using ObtainiumPC.Sources.NeutronCode;
using ObtainiumPC.Sources.SourceForge;
using ObtainiumPC.Sources.SourceHut;
using Xunit;

namespace ObtainiumPC.Tests;

/// Hermetic fixture tests for the installer-capable resolvers that had none:
/// Jenkins, SourceForge, NeutronCode, SourceHut, Direct File Link. Expected values
/// are derived from the upstream algorithms the sources port (jenkins.dart,
/// sourceforge.dart, neutroncode.dart, sourcehut.dart, direct_apk_link.dart).
public class InstallerSourceFixtureTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings =
        new Dictionary<string, JsonElement>();

    private static IReadOnlyDictionary<string, JsonElement> Settings(params (string, object)[] kv) =>
        kv.ToDictionary(x => x.Item1, x => JsonSerializer.SerializeToElement(x.Item2));

    // ---- Jenkins -------------------------------------------------------

    [Fact]
    public async Task Jenkins_uses_build_number_and_lists_only_installable_artifacts()
    {
        const string job = "https://ci.example.org/job/app";
        var body = """
            { "number": 42, "timestamp": 1790000000000,
              "artifacts": [
                { "fileName": "app-setup.exe", "relativePath": "out/app-setup.exe" },
                { "fileName": "notes.txt", "relativePath": "out/notes.txt" } ] }
            """;
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(job + "/lastSuccessfulBuild/api/json", body));
        var d = await new JenkinsSource(http).GetLatestAssetDetailsAsync(job, NoSettings);
        Assert.Equal("42", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("app-setup.exe", a.Key);
        Assert.Equal(job + "/lastSuccessfulBuild/artifact/out/app-setup.exe", a.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), d.ReleaseDate);
    }

    [Fact]
    public async Task Jenkins_http_error_surfaces_the_status_code()
    {
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler());
        var ex = await Assert.ThrowsAsync<ObtainiumHttpException>(
            () => new JenkinsSource(http).GetLatestAssetDetailsAsync("https://ci.example.org/job/app", NoSettings));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void Jenkins_standardize_keeps_the_job_url_and_rejects_others()
    {
        var s = new JenkinsSource(FixtureHttpHandler.Client(new FixtureHttpHandler()));
        Assert.Equal("https://ci.example.org/job/app", s.SourceSpecificStandardizeUrl("https://ci.example.org/job/app/42/console"));
        Assert.Throws<InvalidUrlException>(() => s.SourceSpecificStandardizeUrl("https://ci.example.org/view/all"));
    }

    // ---- SourceForge ---------------------------------------------------

    private const string SfRss = """
        <?xml version="1.0"?><rss version="2.0"><channel>
          <item><guid>https://sourceforge.net/projects/foo/files/1.2/foo-1.2-setup.exe/download</guid></item>
          <item><guid>https://sourceforge.net/projects/foo/files/1.2/readme.txt/download</guid></item>
          <item><guid>https://sourceforge.net/projects/foo/files/1.1/foo-1.1-setup.exe/download</guid></item>
        </channel></rss>
        """;

    [Fact]
    public async Task SourceForge_picks_newest_folder_version_and_only_its_installers()
    {
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler()
            .Respond("https://sourceforge.net/projects/foo/rss?path=/", SfRss));
        var src = new SourceForgeSource(http);
        var url = src.SourceSpecificStandardizeUrl("https://sourceforge.net/projects/foo");
        Assert.Equal("https://sourceforge.net/projects/foo/files", url);
        var d = await src.GetLatestAssetDetailsAsync(url, NoSettings);
        Assert.Equal("1.2", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("foo-1.2-setup.exe", a.Key);
    }

    [Fact]
    public async Task SourceForge_empty_feed_is_no_releases()
    {
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler()
            .Respond("https://sourceforge.net/projects/foo/rss?path=/", "<rss><channel></channel></rss>"));
        await Assert.ThrowsAsync<NoReleasesException>(
            () => new SourceForgeSource(http).GetLatestAssetDetailsAsync("https://sourceforge.net/projects/foo/files", NoSettings));
    }

    [Fact]
    public void SourceForge_p_urls_map_to_projects_files_and_foreign_urls_are_invalid()
    {
        var s = new SourceForgeSource(FixtureHttpHandler.Client(new FixtureHttpHandler()));
        Assert.Equal("https://sourceforge.net/projects/foo/files", s.SourceSpecificStandardizeUrl("https://sourceforge.net/p/foo/wiki/Home/"));
        Assert.Throws<InvalidUrlException>(() => s.SourceSpecificStandardizeUrl("https://example.com/projects/foo"));
    }

    // ---- NeutronCode ---------------------------------------------------

    [Fact]
    public async Task NeutronCode_reads_version_filename_and_date_from_the_download_page()
    {
        const string page = "https://neutroncode.com/downloads/file/neutron-player/";
        var html = """
            <html><body>
              <div class="pd-title">Neutron Player</div>
              <div class="pd-filename"><span class="pd-float"> neutron-setup.exe </span></div>
              <span class="pd-version-txt">Version</span> <span>2.5</span>
              <span class="pd-date-txt">Date</span> <span>12 March 2026</span>
              <div class="pd-fdesc"><p>old</p><p>Fixed bugs</p></div>
            </body></html>
            """;
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(page, html));
        var d = await new NeutronCodeSource(http).GetLatestAssetDetailsAsync(page, NoSettings);
        Assert.Equal("2.5", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("https://neutroncode.com/download/neutron-setup.exe", a.Value);
        Assert.Equal(new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero).Date, d.ReleaseDate!.Value.Date);
        Assert.Equal("Fixed bugs", d.ChangeLog);
    }

    [Fact]
    public async Task NeutronCode_page_without_filename_is_no_releases()
    {
        const string page = "https://neutroncode.com/downloads/file/x/";
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(page, "<html></html>"));
        await Assert.ThrowsAsync<NoReleasesException>(
            () => new NeutronCodeSource(http).GetLatestAssetDetailsAsync(page, NoSettings));
    }

    // ---- SourceHut -----------------------------------------------------

    private const string ShBase = "https://git.sr.ht/~u/app";

    [Fact]
    public async Task SourceHut_reads_latest_ref_and_installer_links_from_the_ref_page()
    {
        var rss = $$"""
            <rss version="2.0"><channel>
              <item><title>v1.0</title><guid>{{ShBase}}/refs/v1.0</guid>
                <pubDate>Mon, 05 Jan 2026 10:00:00 +0000</pubDate><author>alice</author></item>
            </channel></rss>
            """;
        var page = $"""<html><body><a href="{ShBase}/refs/download/v1.0/app-setup.exe">dl</a><a href="/x/notes.txt">n</a></body></html>""";
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler()
            .Respond(ShBase + "/refs/rss.xml", rss)
            .Respond(ShBase + "/refs/v1.0", page));
        var d = await new SourceHutSource(http).GetLatestAssetDetailsAsync(ShBase, Settings(("fallbackToOlderReleases", true)));
        Assert.Equal("v1.0", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("app-setup.exe", a.Key);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero), d.ReleaseDate);
    }

    [Fact]
    public async Task SourceHut_release_without_installer_falls_through_to_no_releases()
    {
        var rss = $$"""
            <rss version="2.0"><channel>
              <item><title>v1.0</title><guid>{{ShBase}}/refs/v1.0</guid></item>
            </channel></rss>
            """;
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler()
            .Respond(ShBase + "/refs/rss.xml", rss)
            .Respond(ShBase + "/refs/v1.0", "<html><a href=\"/notes.txt\">n</a></html>"));
        await Assert.ThrowsAsync<NoReleasesException>(
            () => new SourceHutSource(http).GetLatestAssetDetailsAsync(ShBase, Settings(("fallbackToOlderReleases", true))));
    }

    // ---- Direct File Link ---------------------------------------------

    [Fact]
    public void DirectFileLink_accepts_installer_urls_for_selection_and_rejects_pages()
    {
        var s = new DirectFileLinkSource();
        Assert.True(s.VersionDetectionDisallowed);
        Assert.Equal("https://example.com/dl/app-setup.exe",
            s.SourceSpecificStandardizeUrl("https://example.com/dl/app-setup.exe", forSelection: true));
        Assert.Throws<InvalidUrlException>(() =>
            s.SourceSpecificStandardizeUrl("https://example.com/download-page", forSelection: true));
    }
}
