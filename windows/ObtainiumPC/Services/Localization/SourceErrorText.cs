using System;
using ObtainiumPC.Sources;

namespace ObtainiumPC.Services.Localization;

/// <summary>
/// Presentation-layer text for source exceptions. The raw exception (type,
/// message, stack) stays in the logs; the user sees a localized sentence.
/// Unknown exceptions keep their original message.
/// </summary>
public static class SourceErrorText
{
    /// Upstream en.json key (translated in every supported language).
    public const string NoSuitableReleaseKey = "Could not find a suitable release";

    /// Port-only keys (no upstream en.json entry): Arabic in Loc.ArOverlay. {0} is replaced.
    public const string HttpFailedKey = "Server request failed (HTTP {0})";
    public const string InvalidUrlKey = "Invalid URL for source {0}";

    public static string Describe(Exception ex, Func<string, string> translate) => ex switch
    {
        NoReleasesException => translate(NoSuitableReleaseKey),
        NoAppAssetException when ex.Message == NoAppAssetException.NoWindowsInstallerMessage
            => translate(NoAppAssetException.NoWindowsInstallerMessage),
        NoAppAssetException => translate("No APK found") is var t && t != "No APK found" ? t : ex.Message,
        ObtainiumPC.Sources.FDroid.NoVersionException => translate("Could not determine release version"),
        RepositoryRenamedException r => translate("Repository moved") + ": " + r.OldUrl + " -> " + r.NewUrl,
        ObtainiumHttpException h => translate(HttpFailedKey).Replace("{0}", h.StatusCode.ToString()),
        InvalidUrlException when ex.Message.StartsWith("Invalid URL for source ", StringComparison.Ordinal)
            => translate(InvalidUrlKey).Replace("{0}", ex.Message.Substring("Invalid URL for source ".Length)),
        AndroidSourceNotSupportedException => translate(AndroidSourceNotSupportedException.Text),
        _ => ex.Message,
    };
}
