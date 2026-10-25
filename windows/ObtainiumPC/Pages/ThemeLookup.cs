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
        var themeKey = App.MainWindowInstance?.Content is FrameworkElement root
            && root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        var themed = FindInThemeDictionaries<T>(app.Resources, themeKey, key);
        var fallback = themed == null ? ThemeLookupCore.TryGet<T>(app.Resources, key) : null;
        Services.Logging.AppLogger.Debug(
            $"ThemeLookup {key}: theme={themeKey} themedHit={themed != null} fallbackHit={fallback != null}");
        return themed ?? fallback;
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
