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
}
