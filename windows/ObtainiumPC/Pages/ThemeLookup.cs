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
    public static Brush? Brush(string key) =>
        ThemeLookupCore.TryGet<Brush>(Application.Current is { } app ? app.Resources : null, key);

    public static Style? Style(string key) =>
        ThemeLookupCore.TryGet<Style>(Application.Current is { } app ? app.Resources : null, key);
}
