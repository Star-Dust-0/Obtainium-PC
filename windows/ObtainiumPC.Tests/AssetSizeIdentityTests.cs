using ObtainiumPC.Models;
using Xunit;

namespace ObtainiumPC.Tests;

public class AssetSizeIdentityTests
{
    private static TrackedApp App(string v) => new() { LatestVersion = v };

    [Fact]
    public void SizeIsKnownForTheSameReleaseAndFile()
    {
        var a = App("1.0.1"); a.RecordAssetSize(6_000_000, "https://x/a.exe");
        Assert.Equal(6_000_000, a.KnownSizeFor("https://x/a.exe"));
    }

    [Fact]
    public void SizeFromOlderReleaseIsUnknownForNewerRelease()
    {
        var a = App("1.0.0"); a.RecordAssetSize(6_000_000, "https://x/a.exe");
        a.LatestVersion = "1.0.1";
        Assert.Null(a.KnownSizeFor("https://x/a.exe"));
    }

    [Fact]
    public void SizeFromAnotherAssetIsUnknown()
    {
        var a = App("1.0.1"); a.RecordAssetSize(6_000_000, "https://x/a.exe");
        Assert.Null(a.KnownSizeFor("https://x/b.zip"));
    }

    [Fact]
    public void LegacySizeWithoutIdentityIsUnknown()
    {
        var a = App("1.0.1"); a.AssetSizeBytes = 6_000_000;
        Assert.Null(a.KnownSizeFor("https://x/a.exe"));
    }

    [Fact]
    public void ZeroSizeIsUnknown()
    {
        var a = App("1.0.1"); a.RecordAssetSize(0, "https://x/a.exe");
        Assert.Null(a.KnownSizeFor("https://x/a.exe"));
    }

    private static TrackedApp TwoAssets(int preferred)
    {
        var a = App("1.0.1");
        a.AssetUrls = new List<KeyValuePair<string, string>> { new("setup.exe", "https://x/setup.exe"), new("portable.zip", "https://x/portable.zip") };
        a.PreferredAssetIndex = preferred;
        return a;
    }

    [Fact]
    public void DownloadingTheNonPreferredAssetShowsNoSizeOnTheDetailPage()
    {
        var a = TwoAssets(0); a.RecordAssetSize(6_000_000, "https://x/portable.zip");
        Assert.Equal("https://x/setup.exe", a.PreferredAssetUrl());
        Assert.Null(a.KnownPreferredSize());
    }

    [Fact]
    public void DownloadingThePreferredAssetShowsItsSize()
    {
        var a = TwoAssets(1); a.RecordAssetSize(6_000_000, "https://x/portable.zip");
        Assert.Equal(6_000_000, a.KnownPreferredSize());
    }

    [Fact]
    public void OutOfRangePreferredIndexIsClampedAndEmptyAssetsHaveNoSize()
    {
        var a = TwoAssets(9); a.RecordAssetSize(5, "https://x/portable.zip");
        Assert.Equal("https://x/portable.zip", a.PreferredAssetUrl());
        Assert.Equal(5, a.KnownPreferredSize());
        var none = App("1"); Assert.Null(none.PreferredAssetUrl()); Assert.Null(none.KnownPreferredSize());
    }
}
