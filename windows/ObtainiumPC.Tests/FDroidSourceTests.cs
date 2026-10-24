using System.Net;
using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.FDroid;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/fdroid.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201 (selection order, suggested
/// version code handling, filter regex, versionCode-as-version), not from the
/// C# output.
/// </summary>
public class FDroidSourceTests
{
    private const string PackagesUrl = "https://f-droid.org/api/v1/packages/org.example.app";
    private const string StandardUrl = "https://f-droid.org/packages/org.example.app";

    // Upstream: response['packages'] used newest-first; suggestedVersionCode
    // honored when the toggle is on and no filter is set.
    private const string ApiBody = """
        {
          "packageName": "org.example.app",
          "suggestedVersionCode": 3,
          "packages": [
            { "versionName": "1.2", "versionCode": 3 },
            { "versionName": "1.2", "versionCode": 2 },
            { "versionName": "1.1", "versionCode": 1 }
          ]
        }
        """;

    private static FDroidSource Source(FixtureHttpHandler handler) =>
        new(FixtureHttpHandler.Client(handler));

    private static IReadOnlyDictionary<string, JsonElement> Settings(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => JsonSerializer.SerializeToElement(e.Value));

    [Fact]
    public async Task Suggested_version_code_wins_when_toggle_on()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, ApiBody));
        var details = await source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings(("trySelectingSuggestedVersionCode", true)));
        Assert.Equal("1.2", details.Version);
        Assert.Single(details.AssetUrls);
        Assert.Equal("https://f-droid.org/repo/org.example.app_3.apk", details.AssetUrls[0].Value);
    }

    [Fact]
    public async Task First_release_wins_without_suggested_toggle()
    {
        // Upstream defaults to releases[0] (newest-first array).
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, ApiBody));
        var details = await source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings());
        Assert.Equal("1.2", details.Version);
        Assert.Equal(2, details.AssetUrls.Count); // both builds of 1.2
    }

    [Fact]
    public async Task Version_filter_regex_selects_first_match()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, ApiBody));
        var details = await source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings(("filterVersionsByRegEx", "^1\\.1$")));
        Assert.Equal("1.1", details.Version);
        Assert.Single(details.AssetUrls);
        Assert.Equal("https://f-droid.org/repo/org.example.app_1.apk", details.AssetUrls[0].Value);
    }

    [Fact]
    public async Task Unmatched_filter_throws_no_version()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, ApiBody));
        await Assert.ThrowsAsync<NoVersionException>(() => source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings(("filterVersionsByRegEx", "^9"))));
    }

    [Fact]
    public async Task Version_code_as_os_version_replaces_name()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, ApiBody));
        var details = await source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings(("trySelectingSuggestedVersionCode", true), ("useVersionCodeAsOSVersion", true)));
        Assert.Equal("3", details.Version);
    }

    [Fact]
    public async Task Empty_package_list_throws_no_releases()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, """{"packages": []}"""));
        await Assert.ThrowsAsync<NoReleasesException>(() => source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings()));
    }

    [Fact]
    public async Task Http_error_maps_to_obtainium_http_exception()
    {
        var source = Source(new FixtureHttpHandler().Respond(PackagesUrl, "", HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<ObtainiumHttpException>(() => source.GetLatestAssetDetailsAsync(
            StandardUrl, Settings()));
        Assert.Equal(404, ex.StatusCode);
    }
}
