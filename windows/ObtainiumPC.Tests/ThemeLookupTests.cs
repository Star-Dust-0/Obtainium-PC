using ObtainiumPC.Pages;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Pins the contract behind the Logs-page crash fix: theme lookup must never
/// throw, whatever the dictionary holds. (The WinUI Brush/Style overloads need
/// a running app and stay untestable here; the generic core is the part that
/// decides throw-vs-null.)
/// </summary>
public class ThemeLookupTests
{
    [Fact]
    public void Null_dictionary_yields_null() =>
        Assert.Null(ThemeLookup.TryGet<object>(null, "Anything"));

    [Fact]
    public void Missing_key_yields_null_instead_of_throwing()
    {
        var resources = new Dictionary<object, object>();
        Assert.Null(ThemeLookup.TryGet<object>(resources, "CardBackgroundFillColorDefaultBrush"));
    }

    [Fact]
    public void Wrong_type_yields_null_instead_of_invalid_cast()
    {
        var resources = new Dictionary<object, object> { ["k"] = "not-a-brush" };
        Assert.Null(ThemeLookup.TryGet<Version>(resources, "k"));
    }

    [Fact]
    public void Present_key_with_matching_type_is_returned()
    {
        var v = new Version(1, 0);
        var resources = new Dictionary<object, object> { ["k"] = v };
        Assert.Same(v, ThemeLookup.TryGet<Version>(resources, "k"));
    }
}
