using ObtainiumPC.Services.Versioning;
using Xunit;

namespace ObtainiumPC.Tests;

/// "Hide downgrades" setting: with it on, an installed version newer than the latest is not offered as an update;
/// with it off, any difference is offered. Not-installed and equal versions are never updates.
public class HideDowngradesTests
{
    [Theory]
    [InlineData("1.0", "2.0", true, true)]    // real upgrade, hide on
    [InlineData("1.0", "2.0", false, true)]   // real upgrade, hide off
    [InlineData("3", "2", true, false)]       // downgrade hidden
    [InlineData("3", "2", false, true)]       // downgrade offered when the setting is off
    [InlineData("2.0", "2.0", true, false)]   // equal
    [InlineData("2.0", "2.0", false, false)]
    [InlineData("1.10", "1.9", true, false)]  // numeric, not lexical, comparison
    [InlineData("1.9", "1.10", true, true)]
    public void Updateable_matrix(string installed, string latest, bool hide, bool expected)
        => Assert.Equal(expected, VersionService.IsAppUpdateable(installed, latest, hide));

    [Fact]
    public void Not_installed_is_never_an_update()
    {
        Assert.False(VersionService.IsAppUpdateable(null, "1.0", true));
        Assert.False(VersionService.IsAppUpdateable(null, "1.0", false));
    }

    [Fact]
    public void Uncomparable_versions_stay_offered_with_hide_on()
        => Assert.True(VersionService.IsAppUpdateable("nightly-abc", "release-xyz", true));
}
