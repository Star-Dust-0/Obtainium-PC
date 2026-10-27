using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

/// Port of test/min_update_age_test.dart (isReleaseTooYoung / suppression decision).
public class MinUpdateAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void YoungerThanMinimumAgeIsSuppressed() =>
        Assert.True(AssetFilterService.ShouldSuppressYoungRelease("1.0", "1.1", Now.AddDays(-2), 7, Now));

    [Fact]
    public void OldEnoughIsNotSuppressed() =>
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("1.0", "1.1", Now.AddDays(-8), 7, Now));

    [Fact]
    public void WithoutReleaseDateIsNotSuppressed() =>
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("1.0", "1.1", null, 7, Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DisabledMinimumAgeNeverSuppresses(int days) =>
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("1.0", "1.1", Now.AddHours(-1), days, Now));

    [Fact]
    public void SameVersionIsNeverSuppressed() =>
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("1.1", "1.1", Now.AddHours(-1), 7, Now));

    [Fact]
    public void RecordWithoutStoredLatestVersionIsNeverSuppressed()
    {
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease(null, "1.1", Now.AddHours(-1), 7, Now));
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("", "1.1", Now.AddHours(-1), 7, Now));
    }

    [Fact]
    public void ExactlyAtMinimumAgeIsNotSuppressed() =>
        Assert.False(AssetFilterService.ShouldSuppressYoungRelease("1.0", "1.1", Now.AddDays(-7), 7, Now));
}
