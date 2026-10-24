using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ObtainiumPC.Pages;

/// <summary>
/// Safe programmatic theme-resource lookup. WinUI theme brushes/styles are
/// not guaranteed to be present on Application.Resources at runtime, and the
/// ResourceDictionary indexer throws when a key is missing - an unhandled
/// throw during page load stows and kills the process (0xC000027B).
/// </summary>
public static class ThemeLookup
{
    public static Brush? Brush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : null;

    public static Style? Style(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Style s ? s : null;
}
