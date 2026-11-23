using ObtainiumPC.Sources;

namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Failure categories and apps-page status text for failed update checks.
/// Persisted state holds ONLY an allowlisted category string (an HTTP status
/// code is the only dynamic value) - never raw exception messages, URLs,
/// paths, or response text, which can carry credentials or private repo
/// names. The raw exception stays in the session log, as before.
/// </summary>
public static class CheckFailureText
{
    public const string Unknown = "unknown";

    /// Map a check failure to its allowlisted category.
    public static string CategoryFor(Exception ex) => ex switch
    {
        RateLimitException => "rate_limited",
        // GitHubSource.RateLimitError throws this type with a fixed prefix.
        InvalidOperationException i when i.Message.StartsWith("GitHub rate limit reached", StringComparison.Ordinal) => "rate_limited",
        ObtainiumHttpException h => "http_" + h.StatusCode,
        NoReleasesException => "no_suitable_release",
        NoAppAssetException => "no_installer_asset",
        RepositoryRenamedException => "repository_moved",
        InvalidUrlException => "invalid_url",
        AndroidSourceNotSupportedException => "unsupported_source",
        HttpRequestException => "network_error",
        OperationCanceledException => "timeout",
        _ => Unknown,
    };

    /// Short, localized, category-only reason. Unlisted categories become a
    /// generic unknown so a malformed or legacy persisted value can never
    /// echo raw text into the UI.
    public static string ReasonFor(string? category, Func<string, string> translate)
    {
        if (string.IsNullOrWhiteSpace(category)) return "";
        if (category.StartsWith("http_", StringComparison.Ordinal)
            && int.TryParse(category.Substring(5), out var code) && code >= 100 && code <= 599)
            return translate(Localization.SourceErrorText.HttpFailedKey).Replace("{0}", code.ToString());
        return category switch
        {
            "rate_limited" => "GitHub rate limit reached - add a personal access token in Settings to raise the limit",
            "no_suitable_release" => translate(Localization.SourceErrorText.NoSuitableReleaseKey),
            "no_installer_asset" => "No Windows installer asset in the release",
            "repository_moved" => translate("Repository moved"),
            "invalid_url" => "Invalid URL",
            "unsupported_source" => AndroidSourceNotSupportedException.Text,
            "network_error" => "Network error",
            "timeout" => "Request timed out",
            _ => "Unknown error - details in Logs",
        };
    }

    public static string Format(int consecutiveFailedChecks, string? category, Func<string, string> translate)
    {
        var baseText = $"Last check failed ({consecutiveFailedChecks}x)";
        var reason = ReasonFor(category, translate);
        return reason.Length == 0 ? baseText : baseText + ": " + reason;
    }
}
