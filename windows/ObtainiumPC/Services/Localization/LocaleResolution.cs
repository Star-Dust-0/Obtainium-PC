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
    public static string Resolve(string? systemTag)
    {
        if (!string.IsNullOrEmpty(systemTag))
        {
            var langs = LocTranslations.SupportedLanguages;
            foreach (var (t, _) in langs)
                if (string.Equals(systemTag, t, StringComparison.OrdinalIgnoreCase)) return t;
            var two = systemTag.Split('-')[0];
            if (two.Equals("zh", StringComparison.OrdinalIgnoreCase))
                // Upstream ships traditional (zh-Hant-TW) before simplified
                // (zh), so a generic prefix match would wrongly send zh-CN to
                // the traditional table. Traditional markers decide.
                return systemTag.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                    || systemTag.Contains("-TW", StringComparison.OrdinalIgnoreCase)
                    || systemTag.Contains("-HK", StringComparison.OrdinalIgnoreCase)
                    || systemTag.Contains("-MO", StringComparison.OrdinalIgnoreCase)
                    ? "zh-Hant-TW" : "zh";
            foreach (var (t, _) in langs)
                if (t.Split('-')[0].Equals(two, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return "en";
    }
}
