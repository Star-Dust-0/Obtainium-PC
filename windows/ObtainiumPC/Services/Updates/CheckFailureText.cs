namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Apps-page status text for failed update checks. Pure/static so the test
/// project can link it without WinUI. The full exception (type, message,
/// stack) stays in the logs; this only carries the short reason inline.
/// </summary>
public static class CheckFailureText
{
    /// Longest failure reason shown inline; the full text stays in Logs.
    public const int MaxReasonLength = 120;

    public static string Format(int consecutiveFailedChecks, string? lastCheckError)
    {
        var baseText = $"Last check failed ({consecutiveFailedChecks}x)";
        if (string.IsNullOrWhiteSpace(lastCheckError)) return baseText;
        var reason = lastCheckError.Trim().Replace('\n', ' ').Replace('\r', ' ');
        if (reason.Length > MaxReasonLength) reason = reason[..MaxReasonLength].TrimEnd() + "\u2026";
        return baseText + ": " + reason;
    }
}
