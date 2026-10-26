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
            A("app-release.apk"), A("app-release.apk.sha256"), A("mapping.txt"),
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
}
