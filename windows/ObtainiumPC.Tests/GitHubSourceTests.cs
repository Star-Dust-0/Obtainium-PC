using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.GitHub;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Regression tests for the final_url bug: asset [name,url] pairs must be read
/// from each asset's "final_url" entry (upstream github.dart: e['final_url']
/// as MapEntry). Reading the asset object itself silently produced ('','')
/// pairs and filtered out every release of every repo.
/// Expectations derived from lib/app_sources/github.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: container files prefer the API
/// "url" field; other files prefer "browser_download_url"; prereleases are
/// skipped unless includePrereleases; without fallbackToOlderReleases only
/// the first non-skipped release is considered.
/// </summary>
public class GitHubSourceTests
{
    private const string RepoUrl = "https://github.com/owner/app";
    private const string ReleasesUrl = "https://api.github.com/repos/owner/app/releases?per_page=100";

    private const string Releases = """
        [
          {
            "tag_name": "v2.5.0", "name": "v2.5.0", "prerelease": true, "draft": false,
            "published_at": "2026-10-01T00:00:00Z", "body": "pre",
            "tarball_url": "https://api.github.com/repos/owner/app/tarball/v2.5.0",
            "zipball_url": "https://api.github.com/repos/owner/app/zipball/v2.5.0",
            "assets": [
              { "name": "app-2.5.0-win-x64.exe",
                "url": "https://api.github.com/repos/owner/app/releases/assets/3",
                "browser_download_url": "https://github.com/owner/app/releases/download/v2.5.0/app-2.5.0-win-x64.exe" }
            ]
          },
          {
            "tag_name": "v2.0.0", "name": "v2.0.0", "prerelease": false, "draft": false,
            "published_at": "2026-09-01T00:00:00Z", "body": "stable",
            "tarball_url": "https://api.github.com/repos/owner/app/tarball/v2.0.0",
            "zipball_url": "https://api.github.com/repos/owner/app/zipball/v2.0.0",
            "assets": [
              { "name": "app-2.0.0-win-x64.exe",
                "url": "https://api.github.com/repos/owner/app/releases/assets/1",
                "browser_download_url": "https://github.com/owner/app/releases/download/v2.0.0/app-2.0.0-win-x64.exe" },
              { "name": "app-2.0.0-sha256.txt",
                "url": "https://api.github.com/repos/owner/app/releases/assets/2",
                "browser_download_url": "https://github.com/owner/app/releases/download/v2.0.0/app-2.0.0-sha256.txt" }
            ]
          },
          {
            "tag_name": "v1.0.0", "name": "v1.0.0", "prerelease": false, "draft": false,
            "published_at": "2026-08-01T00:00:00Z", "body": "old",
            "tarball_url": "https://api.github.com/repos/owner/app/tarball/v1.0.0",
            "zipball_url": "https://api.github.com/repos/owner/app/zipball/v1.0.0",
            "assets": []
          }
        ]
        """;

    private static GitHubSource Source() => new(
        // Hermetic: no repo-rename/archive probes, which use their own client.
        new GitHubSourceConfig(CheckRepoRename: false, UpdateHealthArchivedEnabled: false),
        FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(ReleasesUrl, Releases)));

    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings =
        new Dictionary<string, JsonElement>();

    [Fact]
    public async Task Latest_stable_release_resolves_with_real_asset_pairs()
    {
        var details = await Source().GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        Assert.Equal("v2.0.0", details.Version);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("app-2.0.0-win-x64.exe", asset.Key);
        // Container files prefer the API asset URL (upstream getGitHubReleaseAssets).
        Assert.Equal("https://api.github.com/repos/owner/app/releases/assets/1", asset.Value);
        // The regression this pins: no empty ('','') pairs anywhere.
        Assert.DoesNotContain(details.AssetUrls, a => a.Key == "" || a.Value == "");
        Assert.DoesNotContain(details.AllAssetUrls, a => a.Key == "" || a.Value == "");
    }

    [Fact]
    public async Task Non_container_assets_prefer_browser_download_url_in_all_assets()
    {
        var details = await Source().GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        Assert.Contains(details.AllAssetUrls,
            a => a.Key == "app-2.0.0-sha256.txt"
              && a.Value == "https://github.com/owner/app/releases/download/v2.0.0/app-2.0.0-sha256.txt");
    }

    [Fact]
    public async Task Prereleases_are_skipped_by_default()
    {
        var details = await Source().GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        Assert.Equal("v2.0.0", details.Version);
    }

    [Fact]
    public async Task IncludePrereleases_selects_the_prerelease()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["includePrereleases"] = JsonSerializer.SerializeToElement(true),
        };
        var details = await Source().GetLatestAssetDetailsAsync(RepoUrl, settings);
        Assert.Equal("v2.5.0", details.Version);
        Assert.Contains(details.AssetUrls, a => a.Key == "app-2.5.0-win-x64.exe");
    }

    private const string ApkOnlyReleases = """
        [
          {
            "tag_name": "v1.7.3", "name": "v1.7.3", "prerelease": false, "draft": false,
            "published_at": "2026-09-01T00:00:00Z", "body": "apk only",
            "assets": [
              { "name": "app-arm64-v8a-release.apk",
                "url": "https://api.github.com/repos/owner/app/releases/assets/9",
                "browser_download_url": "https://github.com/owner/app/releases/download/v1.7.3/app-arm64-v8a-release.apk" }
            ]
          }
        ]
        """;

    private static GitHubSource ApkOnlySource() => new(
        new GitHubSourceConfig(CheckRepoRename: false, UpdateHealthArchivedEnabled: false),
        FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(ReleasesUrl, ApkOnlyReleases)));

    [Fact]
    public async Task Releases_without_any_windows_installer_asset_throw_a_distinct_error()
    {
        var ex = await Assert.ThrowsAsync<NoAppAssetException>(
            () => ApkOnlySource().GetLatestAssetDetailsAsync(RepoUrl, NoSettings));
        Assert.Contains("none has a Windows installer asset", ex.Message);
        Assert.DoesNotContain("No releases found", ex.Message);
    }

    [Fact]
    public async Task TrackOnly_still_resolves_an_apk_only_repo_and_lists_the_apk_as_an_asset()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["trackOnly"] = JsonSerializer.SerializeToElement(true),
        };
        var details = await ApkOnlySource().GetLatestAssetDetailsAsync(RepoUrl, settings);
        Assert.Equal("v1.7.3", details.Version);
        Assert.Contains(details.AllAssetUrls, a => a.Key == "app-arm64-v8a-release.apk");
    }
}
