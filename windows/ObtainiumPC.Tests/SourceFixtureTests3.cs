using System.Net;
using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.Codeberg;
using ObtainiumPC.Sources.GitLab;
using ObtainiumPC.Sources.Html;
using Xunit;

namespace ObtainiumPC.Tests;

/// Fixture tests for GitLab, Codeberg/Forgejo and HTML (gitlab.dart, the Forgejo
/// delegation to the GitHub pipeline, html.dart) with Windows asset mapping.
public class ForgeAndHtmlSourceFixtureTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings =
        new Dictionary<string, JsonElement>();

    private static IReadOnlyDictionary<string, JsonElement> Settings(params (string, object)[] kv) =>
        kv.ToDictionary(x => x.Item1, x => JsonSerializer.SerializeToElement(x.Item2));

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode, string)> _route;
        public List<string> Requests { get; } = new();
        public RouteHandler(Func<string, (HttpStatusCode, string)> route) => _route = route;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.OriginalString;
            Requests.Add(url);
            var (status, body) = _route(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    // ---- GitLab --------------------------------------------------------

    private static RouteHandler GitLabRoutes() => new(url =>
    {
        if (url.Contains("/releases?"))
            return (HttpStatusCode.OK, """
                [
                  { "tag_name": "v3.0", "released_at": "2026-10-01T00:00:00Z",
                    "description": "Changes [setup](/uploads/abc/app-3.0.msi)",
                    "assets": { "links": [
                      { "name": "app-setup.exe", "direct_asset_url": "https://gitlab.com/o/r/-/releases/v3.0/downloads/app-setup.exe", "link_type": "package" },
                      { "name": "app.apk", "direct_asset_url": "https://gitlab.com/o/r/-/releases/v3.0/downloads/app.apk", "link_type": "other" } ] } },
                  { "tag_name": "v2.0", "released_at": "2026-09-01T00:00:00Z", "description": "",
                    "assets": { "links": [ { "name": "old.exe", "url": "https://gitlab.com/o/r/old.exe" } ] } }
                ]
                """);
        if (url.Contains("/repository/tags?"))
            return (HttpStatusCode.OK, """[ { "name": "v9.9", "commit": { "created_at": "2026-10-02T00:00:00Z" } } ]""");
        if (url.Contains("/api/v4/projects/"))
            return (HttpStatusCode.OK, """{ "id": 123, "archived": false }""");
        return (HttpStatusCode.NotFound, "");
    });

    [Fact]
    public async Task GitLab_selects_newest_release_with_installer_links_and_description_uploads()
    {
        var src = new GitLabSource(null, new HttpClient(GitLabRoutes()));
        var d = await src.GetLatestAssetDetailsAsync("https://gitlab.com/o/r", NoSettings);
        Assert.Equal("v3.0", d.Version);
        Assert.Contains(d.AssetUrls, a => a.Key == "app-setup.exe");
        Assert.Contains(d.AssetUrls, a => a.Key == "app-3.0.msi" && a.Value == "https://gitlab.com/-/project/123/uploads/abc/app-3.0.msi");
        Assert.DoesNotContain(d.AssetUrls, a => a.Key == "app.apk");
        Assert.False(d.IsArchived);
    }

    [Fact]
    public async Task GitLab_trackOnly_reads_tags_instead_of_releases()
    {
        var routes = GitLabRoutes();
        var src = new GitLabSource(null, new HttpClient(routes));
        var d = await src.GetLatestAssetDetailsAsync("https://gitlab.com/o/r", Settings(("trackOnly", true)));
        Assert.Equal("v9.9", d.Version);
        Assert.Contains(routes.Requests, r => r.Contains("/repository/tags?"));
        Assert.DoesNotContain(routes.Requests, r => r.Contains("/releases?"));
    }

    [Fact]
    public async Task GitLab_private_token_is_added_only_when_configured_and_http_errors_surface()
    {
        var routes = GitLabRoutes();
        var src = new GitLabSource(new GitLabSourceConfig(PersonalAccessToken: "tok"), new HttpClient(routes));
        await src.GetLatestAssetDetailsAsync("https://gitlab.com/o/r", NoSettings);
        Assert.All(routes.Requests, r => Assert.Contains("private_token=tok", r));
        var notFound = new GitLabSource(null, new HttpClient(new RouteHandler(_ => (HttpStatusCode.NotFound, ""))));
        var ex = await Assert.ThrowsAsync<ObtainiumHttpException>(
            () => notFound.GetLatestAssetDetailsAsync("https://gitlab.com/o/r", NoSettings));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void GitLab_package_token_and_extension_rules_for_installable_assets()
    {
        Assert.True(GitLabSource.IsInstallableAsset("app-setup.exe", "", "other"));
        Assert.True(GitLabSource.IsInstallableAsset("Windows installer", "https://x/dl", "package"));
        Assert.False(GitLabSource.IsInstallableAsset("Windows installer", "https://x/dl", "other"));
        Assert.False(GitLabSource.IsInstallableAsset("app.apk", "https://x/app.apk", "package"));
    }

    [Fact]
    public void GitLab_standardize_cuts_at_dash_and_requires_owner_and_project()
    {
        var s = new GitLabSource();
        Assert.Equal("https://gitlab.com/o/r", s.SourceSpecificStandardizeUrl("https://gitlab.com/o/r/-/releases"));
        Assert.Throws<InvalidUrlException>(() => s.SourceSpecificStandardizeUrl("https://gitlab.com/o"));
    }

    // ---- Codeberg / Forgejo -------------------------------------------

    [Fact]
    public async Task Codeberg_resolves_releases_through_the_forgejo_api_host()
    {
        const string releases = """
            [ { "tag_name": "v1.4.0", "name": "v1.4.0", "prerelease": false, "draft": false,
                "published_at": "2026-10-01T00:00:00Z", "body": "",
                "assets": [ { "name": "tool-setup.exe",
                  "url": "https://codeberg.org/api/v1/repos/o/r/releases/assets/5",
                  "browser_download_url": "https://codeberg.org/o/r/releases/download/v1.4.0/tool-setup.exe" } ] } ]
            """;
        var routes = new RouteHandler(url => url.StartsWith("https://codeberg.org/api/v1/repos/o/r/releases", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, releases) : (HttpStatusCode.NotFound, ""));
        var src = new CodebergSource(null, new HttpClient(routes));
        var d = await src.GetLatestAssetDetailsAsync("https://codeberg.org/o/r", NoSettings);
        Assert.Equal("v1.4.0", d.Version);
        Assert.Equal("tool-setup.exe", Assert.Single(d.AssetUrls).Key);
        Assert.All(routes.Requests, r => Assert.StartsWith("https://codeberg.org/api/v1/", r));
    }

    [Fact]
    public void Codeberg_standardize_and_changelog_page()
    {
        var s = new CodebergSource();
        Assert.Equal("https://codeberg.org/o/r", s.SourceSpecificStandardizeUrl("https://codeberg.org/o/r/issues"));
        Assert.Equal("https://codeberg.org/o/r/releases", s.ChangeLogPageFromStandardUrl("https://codeberg.org/o/r"));
        Assert.Throws<InvalidUrlException>(() => s.SourceSpecificStandardizeUrl("https://example.com/o/r"));
    }

    // ---- HTML ----------------------------------------------------------

    [Fact]
    public async Task Html_extracts_installer_link_and_version_from_the_link_with_a_regex()
    {
        const string page = "https://example.com/downloads/";
        var html = """
            <html><body>
              <a href="/files/readme.txt">readme</a>
              <a href="/files/app-1.4.2-setup.exe">download</a>
            </body></html>
            """;
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(page, html));
        var d = await new HtmlSource(http).GetLatestAssetDetailsAsync(
            page, Settings(("versionExtractionRegEx", @"(\d+\.\d+\.\d+)")));
        Assert.Equal("1.4.2", d.Version);
        var a = Assert.Single(d.AssetUrls);
        Assert.Equal("https://example.com/files/app-1.4.2-setup.exe", a.Value);
    }

    [Fact]
    public async Task Html_page_without_installer_links_is_no_releases()
    {
        const string page = "https://example.com/downloads/";
        var http = FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(page, "<html><a href=\"/a.txt\">x</a></html>"));
        await Assert.ThrowsAsync<NoReleasesException>(() => new HtmlSource(http).GetLatestAssetDetailsAsync(page, NoSettings));
    }
}
