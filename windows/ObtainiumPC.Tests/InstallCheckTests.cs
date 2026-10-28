using ObtainiumPC.Services.Download;
using Xunit;

namespace ObtainiumPC.Tests;

public class InstallCheckTests
{
    [Theory]
    [InlineData("2.0", "2.0", "1.0", InstallCheckResult.Confirmed)]
    [InlineData("v2.0", "2.0", "1.0", InstallCheckResult.Confirmed)]
    [InlineData("2.0", "v2.0", "1.0", InstallCheckResult.Confirmed)]
    [InlineData("1.0", "2.0", "1.0", InstallCheckResult.Unchanged)]
    [InlineData("1.5", "2.0", "1.0", InstallCheckResult.Different)]
    [InlineData(null, "2.0", "1.0", InstallCheckResult.NotDetected)]
    [InlineData("  ", "2.0", "1.0", InstallCheckResult.NotDetected)]
    public void Evaluate_reads_only_the_detected_version(string? detected, string? latest, string? previous, InstallCheckResult expected) =>
        Assert.Equal(expected, InstallCheck.Evaluate(detected, latest, previous));

    [Fact]
    public void A_first_install_has_no_previous_version_and_only_the_release_confirms()
    {
        Assert.Equal(InstallCheckResult.Confirmed, InstallCheck.Evaluate("2.0", "2.0", null));
        Assert.Equal(InstallCheckResult.Different, InstallCheck.Evaluate("1.0", "2.0", null));
    }

    [Fact]
    public void Same_text_does_not_equal_a_different_digit_after_the_v()
    {
        Assert.Equal(InstallCheckResult.Different, InstallCheck.Evaluate("v1.0.1", "1.0", null));
        Assert.Equal("1.0", InstallCheck.Normalize("V1.0"));
        Assert.Equal("vim", InstallCheck.Normalize("vim"));
    }

    [Fact]
    public void Unconfirmed_messages_say_what_is_known_and_never_claim_success()
    {
        var unchanged = InstallCheck.UnconfirmedMessage(InstallCheckResult.Unchanged, "1.0", 0, true);
        Assert.Contains("still 1.0", unchanged); Assert.Contains("not installed or was cancelled", unchanged);
        var code = InstallCheck.UnconfirmedMessage(InstallCheckResult.Unchanged, "1.0", 1602, true);
        Assert.Contains("exit code 1602", code);
        var unread = InstallCheck.UnconfirmedMessage(InstallCheckResult.NotDetected, null, null, false);
        Assert.Contains("could not be read", unread);
        var diff = InstallCheck.UnconfirmedMessage(InstallCheckResult.Different, "1.5", 0, true);
        Assert.Contains("1.5", diff);
        foreach (var m in new[] { unchanged, code, unread, diff }) Assert.DoesNotContain("successfully", m);
    }
}
