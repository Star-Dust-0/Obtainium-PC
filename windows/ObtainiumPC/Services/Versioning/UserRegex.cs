using System.Text.RegularExpressions;

namespace ObtainiumPC.Services.Versioning;

/// <summary>
/// Builds a Regex from a pattern the user typed or imported (asset filter, version regex,
/// release filters). A match timeout stops a catastrophic pattern from freezing the app.
/// An invalid pattern still throws ArgumentException, as before.
/// </summary>
public static class UserRegex
{
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    public static Regex Create(string pattern, RegexOptions options = RegexOptions.None)
        => new(pattern, options, MatchTimeout);
}
