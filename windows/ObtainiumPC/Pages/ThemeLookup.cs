using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ObtainiumPC.Pages;

/// <summary>
/// Safe programmatic theme-resource lookup. WinUI theme brushes/styles are
/// not guaranteed to be present on Application.Resources at runtime, and the
/// ResourceDictionary indexer throws when a key is missing - an unhandled
/// throw during page load stows and kills the process (0xC000027B). The
/// generic core is BCL-only so the test project can pin the contract.
/// </summary>
public static class ThemeLookup
{
    /// Never throws: null dictionary, missing key, or wrong type all yield null.
    public static T? TryGet<T>(IDictionary<object, object>? resources, string key) where T : class =>
        resources != null && resources.TryGetValue(key, out var v) && v is T t ? t : null;

    public static Brush? Brush(string key) =>
        TryGet<Brush>(Application.Current is { } app ? app.Resources : null, key);

    public static Style? Style(string key) =>
        TryGet<Style>(Application.Current is { } app ? app.Resources : null, key);
}
