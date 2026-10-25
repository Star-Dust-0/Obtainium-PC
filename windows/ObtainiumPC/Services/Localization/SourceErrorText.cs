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

    public static string Describe(Exception ex, Func<string, string> translate) => ex switch
    {
        NoReleasesException => translate(NoSuitableReleaseKey),
        NoAppAssetException when ex.Message == NoAppAssetException.NoWindowsInstallerMessage
            => translate(NoAppAssetException.NoWindowsInstallerMessage),
        _ => ex.Message,
    };
}
