using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class BackgroundCheckGuardsTests
{
    [Fact] public void DefaultsNeverSkip() =>
        Assert.Null(BackgroundCheckGuards.SkipReason(false, false, false, true, true));

    [Fact] public void ManualCheckAlwaysRuns() =>
        Assert.Null(BackgroundCheckGuards.SkipReason(true, true, true, true, true));

    [Fact] public void MeteredGuardSkipsBackgroundOnMetered() =>
        Assert.Equal("metered connection", BackgroundCheckGuards.SkipReason(false, true, false, true, false));

    [Fact] public void MeteredGuardDoesNotSkipOnUnmetered() =>
        Assert.Null(BackgroundCheckGuards.SkipReason(false, true, false, false, false));

    [Fact] public void BatteryGuardSkipsOnBattery() =>
        Assert.Equal("battery power", BackgroundCheckGuards.SkipReason(false, false, true, false, true));

    [Fact] public void BatteryGuardDoesNotSkipWhenPluggedIn() =>
        Assert.Null(BackgroundCheckGuards.SkipReason(false, false, true, false, false));

    [Fact] public void UnknownStateNeverBlocks()
    {
        Assert.Null(BackgroundCheckGuards.SkipReason(false, true, true, null, null));
    }

    [Fact] public void GuardOffIgnoresDetectedCondition() =>
        Assert.Null(BackgroundCheckGuards.SkipReason(false, false, false, true, true));
}
