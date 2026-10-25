using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

public class AssetFilterTests
{
    [Theory]
    [InlineData("app-x86_64.msix", true)]
    [InlineData("app-setup.exe", true)]
    [InlineData("app.zip", false)]
    [InlineData("app.zip", true, true)]
    [InlineData("app.tar.gz", true, false, true)]
    [InlineData("checksums.txt", false)]
    public void Container_classification(string name, bool expected, bool includeArchives = false, bool includeTarballs = false)
    {
        Assert.Equal(expected, AssetFilterService.IsAppOrContainerFile(name, includeArchives, includeTarballs));
    }

    [Fact]
    public void Self_update_release_assets_select_the_x64_installer()
    {
        // Synthetic release metadata for MohamedElnaggar00/Obtainium-PC (the
        // packaging convention in windows/packaging + build-windows.yml): the
        // in-app self-update path - an ordinary tracked app pointing at the
        // fork's repo - must pick the x64 Inno installer. The portable zip
        // stays opt-in (includeArchives), checksums are never apps, and a
        // foreign-arch installer must not win on an x64 device.
        var assets = new List<KeyValuePair<string, string>>
        {
            new("ObtainiumPC-Setup-win-arm64.exe", "https://example.invalid/arm64"),
            new("ObtainiumPC-Setup-win-x64.exe", "https://example.invalid/x64"),
            new("ObtainiumPC-win-x64.zip", "https://example.invalid/zip"),
            new("checksums.txt", "https://example.invalid/sums"),
        };
        var containers = assets.Where(e => AssetFilterService.IsAppOrContainerFile(e.Key)).ToList();
        // Same priority list AppUpdateService.CurrentArches returns on x64.
        var byArch = AssetFilterService.FilterAssetsByArch(containers, new[] { "x64", "arm64", "x86" });
        Assert.Equal("ObtainiumPC-Setup-win-x64.exe", Assert.Single(byArch).Key);

        // With the archives option on, the portable zip becomes a valid
        // alternative but the arch filter still excludes other-arch installers.
        var withZip = assets.Where(e => AssetFilterService.IsAppOrContainerFile(e.Key, includeArchives: true)).ToList();
        Assert.Equal(3, withZip.Count);
        var byArchZip = AssetFilterService.FilterAssetsByArch(withZip, new[] { "x64", "arm64", "x86" });
        Assert.All(byArchZip, e => Assert.DoesNotContain("arm64", e.Key));
    }
}
