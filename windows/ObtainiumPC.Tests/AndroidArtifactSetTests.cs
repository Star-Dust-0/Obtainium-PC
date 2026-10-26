using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

/// Windows-only: releases that carry only Android packages are not addable.
public class AndroidArtifactSetTests
{
    private static KeyValuePair<string, string> A(string name, string url = "https://example.com/dl") => new(name, url);

    [Fact]
    public void Apk_only_release_is_android_only()
    {
        Assert.True(AssetFilterService.IsAndroidOnlyArtifactSet(new[]
        {
            A("app-release.apk"), A("app-release.apk.idsig"), A("app-release.apk.sha256.sig"), A("app-release.apk.sha256"), A("mapping.txt"),
            A("Source code (zip)", "https://api.github.com/repos/o/r/zipball/v1"),
        }));
        Assert.True(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("App-arm64-v8a.APK"), A("App.xapk") }));
    }

    [Fact]
    public void Apk_next_to_a_desktop_artifact_is_kept()
    {
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("app.apk"), A("app-setup.exe") }));
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("app.apk"), A("app-win64.zip") }));
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("app.apk"), A("app.dmg") }));
    }

    [Fact]
    public void Windows_project_without_installers_is_never_android_only()
    {
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("tool-x86_64-pc-windows-msvc.zip"), A("tool.tar.gz") }));
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(Array.Empty<KeyValuePair<string, string>>()));
        Assert.False(AssetFilterService.IsAndroidOnlyArtifactSet(new[] { A("Source code (zip)", "https://api.github.com/repos/o/r/zipball/v1") }));
    }

    private static ObtainiumPC.Models.TrackedApp Rec(params string[] names)
    {
        var app = new ObtainiumPC.Models.TrackedApp { Id = "x", Url = "https://github.com/o/r" };
        app.AssetUrls = names.Select(n => A(n)).ToList();
        return app;
    }

    [Fact]
    public void Stored_record_with_only_apks_is_legacy_android()
        => Assert.True(AssetFilterService.StoredAssetsAreAndroidOnly(Rec("a-arm64.apk", "a-arm64.apk.sha256")));

    [Fact]
    public void Stored_record_with_no_assets_is_unknown_not_android()
        => Assert.False(AssetFilterService.StoredAssetsAreAndroidOnly(Rec()));

    [Fact]
    public void Stored_record_with_a_desktop_artifact_is_kept()
        => Assert.False(AssetFilterService.StoredAssetsAreAndroidOnly(Rec("a.apk", "a-setup.exe")));

    [Fact]
    public void Stored_record_keeps_apks_found_in_other_assets()
    {
        var app = Rec();
        app.OtherAssetUrls = new() { A("a.apk") };
        Assert.True(AssetFilterService.StoredAssetsAreAndroidOnly(app));
    }

    [Fact]
    public void Checking_does_not_modify_the_record()
    {
        var app = Rec("a.apk");
        var before = app.AssetUrlsJson;
        _ = AssetFilterService.StoredAssetsAreAndroidOnly(app);
        Assert.Equal(before, app.AssetUrlsJson);
    }
}
