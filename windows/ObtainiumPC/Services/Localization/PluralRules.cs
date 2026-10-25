using System;
using System.Collections.Generic;

namespace ObtainiumPC.Services.Localization;

/// <summary>
/// CLDR cardinal plural categories for the shipped locales (the rule set
/// easy_localization applies upstream). Missing forms in a language's data
/// fall back to "other" in Loc.Plural, mirroring easy_localization.
/// Integer counts only - the port formats whole counts.
/// </summary>
public static class PluralRules
{
    /// <summary>
    /// Selects the form for n from a language's plural table, falling back to
    /// "other" when the data lacks the category (easy_localization behavior:
    /// upstream's ar.json, for example, only ships one/other), then to the
    /// caller's fallback literal.
    /// </summary>
    public static string SelectForm(IReadOnlyDictionary<string, string> forms, string languageTag, int n, string fallback)
    {
        if (forms.TryGetValue(Select(languageTag, n), out var form)) return form;
        if (forms.TryGetValue("other", out var other)) return other;
        return fallback;
    }

    public static string Select(string languageTag, int n)
    {
        var lang = languageTag.Split('-')[0];
        switch (lang)
        {
            case "ar":
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                var mod100ar = n % 100;
                if (mod100ar >= 3 && mod100ar <= 10) return "few";
                if (mod100ar >= 11 && mod100ar <= 99) return "many";
                return "other";
            case "ru":
            case "uk":
            case "bs":
            {
                var mod10 = n % 10;
                var mod100 = n % 100;
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                if (mod10 == 0 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 11 && mod100 <= 14)) return "many";
                return "other";
            }
            case "pl":
            {
                if (n == 1) return "one";
                var mod10 = n % 10;
                var mod100 = n % 100;
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                if (mod10 == 0 || mod10 == 1 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 12 && mod100 <= 14)) return "many";
                return "other";
            }
            case "cs":
                if (n == 1) return "one";
                if (n >= 2 && n <= 4) return "few";
                return "other";
            case "fr":
            case "pt":
            case "fa":
            case "bn":
                return n is 0 or 1 ? "one" : "other";
            case "ja":
            case "ko":
            case "zh":
            case "vi":
            case "id":
            case "ml":
                return "other";
            default:
                // en, de, nl, sv, da, it, es, ca, gl, hu, tr, en-EO (eo)
                return n == 1 ? "one" : "other";
        }
    }
}
