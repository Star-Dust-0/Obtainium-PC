using ObtainiumPC.Services.Versioning;
using Xunit;

namespace ObtainiumPC.Tests;

public class VersionServiceTests
{
    [Theory]
    [InlineData(null, null, "app-1.2.3-release", "1.2.3")]
    [InlineData("v([0-9.]+)", null, "v2.0.1", "2.0.1")]
    [InlineData("release-(\\d+)-(\\d+)", "2.1", "release-3-7", "7.3")]
    public void ExtractVersion_matches_upstream_behavior(string? regex, string? matchGroup, string input, string expected)
    {
        Assert.Equal(expected, VersionService.ExtractVersion(regex, matchGroup, input));
    }

    [Fact]
    public void ExtractVersion_returns_null_when_nothing_matches()
    {
        Assert.Null(VersionService.ExtractVersion("build-(\\d+)", null, "no digits here"));
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
