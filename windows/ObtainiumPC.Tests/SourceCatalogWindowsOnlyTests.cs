using ObtainiumPC.Services.Settings;
using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

/// Obtainium-PC is Windows-only: Android app stores are not offered and are classified as such.
public class SourceCatalogWindowsOnlyTests
{
    private static readonly string[] Windows =
        { "GitHub", "GitLab", "Forgejo (Codeberg)", "SourceForge", "SourceHut", "itch.io", "Jenkins", "HTML", "Direct File Link" };

    [Fact]
    public void Override_list_is_exactly_the_windows_capable_sources()
    {
        Assert.Equal(Windows, SourceCatalog.OverrideSourceNames);
    }

    [Theory]
    [InlineData("https://f-droid.org/packages/org.fdroid.fdroid/")]
    [InlineData("https://farsroid.com/telegram-x/")]
    [InlineData("https://apkpure.com/x/y")]
    [InlineData("https://www.apkmirror.com/apk/a/b/")]
    [InlineData("https://www.uptodown.com/android")]
    [InlineData("https://apt.izzysoft.de/fdroid/index/apk/a.b")]
    public void Android_hosts_are_classified_android_only(string url)
    {
        var settings = new AppSettings();
        Assert.True(SourceCatalog.IsAndroidOnlyApp(url, null, settings));
    }

    [Theory]
    [InlineData("https://github.com/notepad-plus-plus/notepad-plus-plus")]
    [InlineData("https://gitlab.com/gitlab-org/cli")]
    [InlineData("https://codeberg.org/a/b")]
    [InlineData("https://sourceforge.net/projects/x/")]
    [InlineData("https://git.sr.ht/~a/b")]
    public void Windows_capable_hosts_are_not_android_only(string url)
    {
        Assert.False(SourceCatalog.IsAndroidOnlyApp(url, null, new AppSettings()));
    }

    [Fact]
    public void Unknown_host_is_kept_not_classified_android()
    {
        Assert.False(SourceCatalog.IsAndroidOnlyApp("https://example.invalid/app", null, new AppSettings()));
    }

    [Fact]
    public void Override_source_name_decides_for_android_stores()
    {
        Assert.True(SourceCatalog.IsAndroidOnlyApp("https://example.com/x", "F-Droid", new AppSettings()));
        Assert.False(SourceCatalog.IsAndroidOnlyApp("https://example.com/x", "Direct File Link", new AppSettings()));
    }

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
