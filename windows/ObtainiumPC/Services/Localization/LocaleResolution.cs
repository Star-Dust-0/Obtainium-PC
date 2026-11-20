using System;
using System.Linq;

namespace ObtainiumPC.Services.Localization;

/// <summary>
/// Pure system-locale resolution against the supported language table
/// (upstream: easy_localization device-locale matching with English
/// fallback). WinUI-free so the test project can link it.
/// </summary>
public static class LocaleResolution
{
    /// <summary>
    /// The languages the program offers: English and Arabic. The generated
    /// table still carries the other upstream translations, but they are not
    /// selectable and never picked from the system locale.
    /// </summary>
    public static readonly (string Tag, string Name)[] OfferedLanguages =
        LocTranslations.SupportedLanguages.Where(l => l.Tag is "en" or "ar").ToArray();

    public static bool IsOffered(string? tag) =>
        !string.IsNullOrEmpty(tag) && OfferedLanguages.Any(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The effective language for a stored choice: "" follows the system, an
    /// offered tag is used as is, and a tag that is no longer offered (saved
    /// by an earlier version) falls back to following the system. The stored
    /// value itself is never rewritten here.
    /// </summary>
    public static string ResolveStored(string? stored, Func<string> system) =>
        string.IsNullOrEmpty(stored) || !IsOffered(stored) ? system() : stored;

    public static string Resolve(string? systemTag)
    {
        if (!string.IsNullOrEmpty(systemTag))
        {
            foreach (var (t, _) in OfferedLanguages)
                if (string.Equals(systemTag, t, StringComparison.OrdinalIgnoreCase)) return t;
            var two = systemTag.Split('-')[0];
            foreach (var (t, _) in OfferedLanguages)
                if (t.Equals(two, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return "en";
    }
}
