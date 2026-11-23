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

    /// <summary>
    /// Persisted/displayed failure reasons must never carry secrets: strip
    /// userinfo, query and fragment from any URL (token-in-URL sources), and
    /// common credential key=value pairs outside URLs. Host + path stay so the
    /// reason still identifies the failing endpoint.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var t = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"https?://[^\s""<>()\[\]{}']+",
            m =>
            {
                if (!System.Uri.TryCreate(m.Value, System.UriKind.Absolute, out var u)) return m.Value;
                var port = u.IsDefaultPort ? "" : ":" + u.Port;
                return u.Scheme + "://" + u.Host + port + u.AbsolutePath;
            });
        t = System.Text.RegularExpressions.Regex.Replace(
            t,
            @"(?i)(token|access_token|api[-_]?key|password|passwd|secret|sig|signature|credential)=([^&\s]+)",
            "$1=[redacted]");
        return t;
    }

    public static string Format(int consecutiveFailedChecks, string? lastCheckError)
    {
        var baseText = $"Last check failed ({consecutiveFailedChecks}x)";
        if (string.IsNullOrWhiteSpace(lastCheckError)) return baseText;
        var reason = lastCheckError.Trim().Replace('\n', ' ').Replace('\r', ' ');
        if (reason.Length > MaxReasonLength) reason = reason[..MaxReasonLength].TrimEnd() + "\u2026";
        return baseText + ": " + reason;
    }
}
