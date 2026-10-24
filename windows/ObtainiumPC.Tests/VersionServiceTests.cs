using ObtainiumPC.Services.Versioning;
using Xunit;

namespace ObtainiumPC.Tests;

public class VersionServiceTests
{
    [Fact]
    public void Null_regex_returns_null()
    {
        Assert.Null(VersionService.ExtractVersion(null, null, "app-1.2.3-release"));
    }

    [Fact]
    public void Default_match_group_is_the_whole_match()
    {
        Assert.Equal("v2.0.1", VersionService.ExtractVersion("v([0-9.]+)", null, "v2.0.1"));
    }

    [Fact]
    public void Numeric_match_group_selects_capture_group()
    {
        Assert.Equal("2.0.1", VersionService.ExtractVersion("v([0-9.]+)", "1", "v2.0.1"));
    }

    [Fact]
    public void Dollar_references_compose_groups()
    {
        Assert.Equal("7.3", VersionService.ExtractVersion(@"release-(\d+)-(\d+)", "$2.$1", "release-3-7"));
    }

    [Fact]
    public void No_match_throws()
    {
        Assert.Throws<NoVersionException>(() => VersionService.ExtractVersion(@"build-(\d+)", null, "no digits here"));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.10", -1)]
    [InlineData("2.0", "2.0.0", 0)]
    [InlineData("1.10.0", "1.9.9", 1)]
    public void CompareVersionsNumerically_orders_correctly(string v1, string v2, int expectedSign)
    {
        var result = VersionService.CompareVersionsNumerically(v1, v2);
        Assert.NotNull(result);
        Assert.Equal(expectedSign, Math.Sign(result!.Value));
    }
}
