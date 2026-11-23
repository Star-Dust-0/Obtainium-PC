using System.Text.Json;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class CheckFailureTextTests
{
    [Fact] public void NoReason_KeepsLegacyText() =>
        Assert.Equal("Last check failed (3x)", CheckFailureText.Format(3, null));

    [Fact] public void Reason_IsAppendedAfterCount() =>
        Assert.Equal("Last check failed (1x): Server request failed (HTTP 404)",
            CheckFailureText.Format(1, "Server request failed (HTTP 404)"));

    [Fact] public void WhitespaceReason_FallsBackToLegacyText() =>
        Assert.Equal("Last check failed (2x)", CheckFailureText.Format(2, "   "));

    [Fact] public void LongReason_IsTruncatedWithEllipsis()
    {
        var reason = new string('x', CheckFailureText.MaxReasonLength + 50);
        var text = CheckFailureText.Format(4, reason);
        Assert.EndsWith("\u2026", text);
        Assert.True(text.Length <= "Last check failed (4x): ".Length + CheckFailureText.MaxReasonLength + 1);
    }

    [Fact] public void Newlines_AreFlattened() =>
        Assert.Equal("Last check failed (1x): a b", CheckFailureText.Format(1, " a\r\nb "));

    [Fact] public void Sanitize_StripsQueryUserinfoFragment_KeepsHostAndPath() =>
        Assert.Equal("moved: https://github.com/old/repo -> https://github.com/new/repo",
            CheckFailureText.Sanitize("moved: https://user:secret@github.com/old/repo?token=abc123#frag -> https://github.com/new/repo?x=1"));

    [Fact] public void Sanitize_RedactsCredentialKeyValues() =>
        Assert.Equal("auth failed token=[redacted] next",
            CheckFailureText.Sanitize("auth failed token=abc123XYZ next"));

    [Fact] public void Sanitize_LeavesPlainTextUnchanged() =>
        Assert.Equal("Server request failed (HTTP 404)",
            CheckFailureText.Sanitize("Server request failed (HTTP 404)"));

    [Fact] public void Sanitize_NullBecomesEmpty() =>
        Assert.Equal("", CheckFailureText.Sanitize(null));

    [Fact] public void LastCheckError_RoundTripsThroughJson()
    {
        var app = new TrackedApp { Id = "a", Url = "https://github.com/x/y", ConsecutiveFailedChecks = 2, LastCheckError = "Server request failed (HTTP 404)" };
        var back = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app));
        Assert.NotNull(back);
        Assert.Equal(2, back!.ConsecutiveFailedChecks);
        Assert.Equal("Server request failed (HTTP 404)", back.LastCheckError);
    }

    [Fact] public void LastCheckError_DefaultsToNull_ForOldStores()
    {
        var back = JsonSerializer.Deserialize<TrackedApp>("{\"id\":\"a\",\"url\":\"u\",\"consecutiveFailedChecks\":5}");
        Assert.NotNull(back);
        Assert.Equal(5, back!.ConsecutiveFailedChecks);
        Assert.Null(back.LastCheckError);
    }
}
