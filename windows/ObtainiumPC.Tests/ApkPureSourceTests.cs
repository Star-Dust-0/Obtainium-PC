using System.Text.Json;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.ApkPure;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Expectations derived from lib/app_sources/apkpure.dart @
/// 24807b46a9b2100eabd4c2dd357d84ded0027201: variants group by version_name
/// in first-seen order, asset names are "{appId}-{versionCode}[-arch].{type}",
/// universal/unlimited archs clear the filter list, useFirstApkOfVersion keeps
/// one asset, stayOneVersionBehind skips the newest group, metadata comes from
/// each group's first variant.
/// </summary>
public class ApkPureSourceTests
{
    private const string StoreUrl = "https://apkpure.com/cool-app/com.example.app";
    private const string ApiUrl = "https://tapi.pureapk.com/v3/get_app_his_version?package_name=com.example.app&hl=en";

    private const string Response = """
        {
          "version_list": [
            { "package_name": "com.example.app", "version_name": "3.0", "version_code": 300,
              "title": "Cool App", "developer": "Some Dev", "update_date": "2026-10-01",
              "whatsnew": "new stuff", "native_code": ["universal"],
              "asset": { "type": "APK", "url": "https://dl.apkpure.com/app-300.apk" } },
            { "package_name": "com.example.app", "version_name": "2.0", "version_code": 200,
              "title": "Cool App", "developer": "Some Dev", "update_date": "2026-08-01",
              "whatsnew": "", "native_code": ["arm64-v8a", "armeabi-v7a"],
              "asset": { "type": "APK", "url": "https://dl.apkpure.com/app-200-arm.apk" } },
            { "package_name": "com.example.app", "version_name": "2.0", "version_code": 201,
              "title": "Cool App", "developer": "Some Dev", "update_date": "2026-08-01",
              "native_code": ["x86"],
              "asset": { "type": "APK", "url": "https://dl.apkpure.com/app-201-x86.apk" } }
          ]
        }
        """;

    private static ApkPureSource Source() => new(FixtureHttpHandler.Client(
        new FixtureHttpHandler().Respond(ApiUrl, Response)));
    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    [Fact]
    public async Task Latest_version_resolves_with_first_apk_only_by_default()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["useFirstApkOfVersion"] = JsonSerializer.SerializeToElement(true),
        };
        var details = await Source().GetLatestAssetDetailsAsync(StoreUrl, settings);
        Assert.Equal("3.0", details.Version);
        Assert.Equal("Cool App", details.Names.Name);
        Assert.Equal("Some Dev", details.Names.Author);
        Assert.Equal("new stuff", details.ChangeLog);
        var asset = Assert.Single(details.AssetUrls);
        // apkpure.dart joins native_code BEFORE clearing universal for the arch filter,
        // so the asset key keeps the -universal suffix.
        Assert.Equal("com.example.app-300-universal.apk", asset.Key);
        Assert.Equal("https://dl.apkpure.com/app-300.apk", asset.Value);
    }

    [Fact]
    public async Task All_variants_of_a_version_are_listed_without_the_first_only_setting()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["stayOneVersionBehind"] = JsonSerializer.SerializeToElement(true),
        };
        var details = await Source().GetLatestAssetDetailsAsync(StoreUrl, settings);
        Assert.Equal("2.0", details.Version);
        Assert.Null(details.ChangeLog); // empty whatsnew becomes null
        Assert.Equal(2, details.AssetUrls.Count);
        Assert.Equal("com.example.app-200-arm64-v8a,armeabi-v7a.apk", details.AssetUrls[0].Key);
        Assert.Equal("com.example.app-201-x86.apk", details.AssetUrls[1].Key);
    }

    [Fact]
    public async Task Stay_one_version_behind_without_fallback_throws_when_only_one_version()
    {
        var handler = new FixtureHttpHandler().Respond(ApiUrl, """
            { "version_list": [
                { "package_name": "com.example.app", "version_name": "3.0", "version_code": 300,
                  "asset": { "type": "APK", "url": "https://dl.apkpure.com/app-300.apk" } }
            ] }
            """);
        var settings = new Dictionary<string, JsonElement>
        {
            ["stayOneVersionBehind"] = JsonSerializer.SerializeToElement(true),
        };
        await Assert.ThrowsAsync<NoReleasesException>(
            () => new ApkPureSource(FixtureHttpHandler.Client(handler)).GetLatestAssetDetailsAsync(StoreUrl, settings));
    }

    [Fact]
    public async Task Empty_version_list_is_no_releases()
    {
        var handler = new FixtureHttpHandler().Respond(ApiUrl, """{"version_list": []}""");
        await Assert.ThrowsAsync<NoReleasesException>(
            () => new ApkPureSource(FixtureHttpHandler.Client(handler)).GetLatestAssetDetailsAsync(StoreUrl, NoSettings));
    }
}
