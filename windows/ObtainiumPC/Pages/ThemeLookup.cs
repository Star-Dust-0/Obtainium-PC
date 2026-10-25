using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ObtainiumPC.Pages;

/// <summary>
/// Safe programmatic theme-resource lookup. WinUI theme brushes/styles are
/// not guaranteed to be present on Application.Resources at runtime, and the
/// ResourceDictionary indexer throws when a key is missing - an unhandled
/// throw during page load stows and kills the process (0xC000027B).
/// The no-throw core lives in ThemeLookupCore (BCL-only, unit-tested).
/// </summary>
public static class ThemeLookup
{
    public static Brush? Brush(string key) => TryGet<Brush>(key);

    public static Style? Style(string key) => TryGet<Style>(key);

    // Application.Resources always resolves theme-dictionary values to the
    // default (light) entry, so programmatic card/foreground lookups rendered
    // light-on-dark. Resolve the Dark/Light dictionary against the window
    // root's ActualTheme first, then fall back to the unthemed lookup.
    private static T? TryGet<T>(string key) where T : class
    {
        if (Application.Current is not { } app) return null;
        // The saved setting is the source of truth the window itself applies;
        // ActualTheme on the window root proved unreliable here (read Light
        // while the dark UI was on screen). For "default", follow the system
        // background color.
        //
        // XamlControlsResources has NO "Dark" theme dictionary (verified in
        // the runner): its keys are [Default, Light, HighContrast] and the
        // dark values live under "Default" (the framework's default theme is
        // dark). Dark therefore resolves via "Dark" first, then "Default".
        string[] themeKeys = App.Settings.Theme switch
        {
            "dark" => new[] { "Dark", "Default" },
            "light" => new[] { "Light" },
            _ => new Windows.UI.ViewManagement.UISettings()
                    .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).B < 128
                ? new[] { "Dark", "Default" } : new[] { "Light" },
        };
        foreach (var themeKey in themeKeys)
            if (FindInThemeDictionaries<T>(app.Resources, themeKey, key) is { } themed)
                return themed;
        return ThemeLookupCore.TryGet<T>(app.Resources, key);
    }

    private static T? FindInThemeDictionaries<T>(ResourceDictionary dict, string themeKey, string key) where T : class
    {
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var themed)
            && themed is ResourceDictionary themeDict
            && ThemeLookupCore.TryGet<T>(themeDict, key) is { } hit)
            return hit;
        foreach (var merged in dict.MergedDictionaries)
            if (FindInThemeDictionaries<T>(merged, themeKey, key) is { } mergedHit)
                return mergedHit;
        return null;
    }

}
