namespace ObtainiumPC.Pages;

/// <summary>
/// BCL-only core of ThemeLookup: never throws - null dictionary, missing key,
/// or wrong value type all yield null. Separated so the plain-net8.0 test
/// project can pin the contract without WinUI references.
/// </summary>
public static class ThemeLookupCore
{
    public static T? TryGet<T>(IDictionary<object, object>? resources, string key) where T : class =>
        resources != null && resources.TryGetValue(key, out var v) && v is T t ? t : null;
}
