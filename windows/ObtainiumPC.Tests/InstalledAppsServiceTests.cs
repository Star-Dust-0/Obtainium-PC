using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

public class InstalledAppsServiceTests
{
    private static InstalledAppInfo Entry(string name, string? publisher = null, string? version = "1.0") =>
        new(name, version, null, publisher);

    [Fact]
    public void Exact_normalized_match_wins()
    {
        var installed = new[] { Entry("7-Zip 24.08 (x64)"), Entry("Other App") };
        var match = InstalledAppsService.FindMatch("7-zip2408x64", null, installed);
        Assert.NotNull(match);
        Assert.Equal(InstalledAppsService.MatchConfidence.Exact, match!.Confidence);
    }

    [Fact]
    public void Unique_containment_match_is_strong()
    {
        var installed = new[] { Entry("Firefox ESR"), Entry("Something Else") };
        var match = InstalledAppsService.FindMatch("Firefox", null, installed);
        Assert.NotNull(match);
        Assert.Equal(InstalledAppsService.MatchConfidence.Strong, match!.Confidence);
    }

    [Fact]
    public void Prefix_match_covers_arch_suffix()
    {
        // Real case: "Notepad++" (tracked name) vs the registry's
        // "Notepad++ (64-bit x64)" - only 46% containment overlap, but a
        // clean prefix match, so detection must still succeed.
        var installed = new[] { Entry("Notepad++ (64-bit x64)", "Notepad++ Team", "8.9.8.1"), Entry("Other App") };
        var match = InstalledAppsService.FindMatch("Notepad++", null, installed);
        Assert.NotNull(match);
        Assert.Equal(InstalledAppsService.MatchConfidence.Strong, match!.Confidence);
        Assert.Equal("8.9.8.1", match.Info.DisplayVersion);
    }

    [Fact]
    public void Ambiguous_prefix_returns_null()
    {
        var installed = new[] { Entry("Git (x64)"), Entry("GitHub Desktop") };
        Assert.Null(InstalledAppsService.FindMatch("Git", null, installed));
    }

    [Fact]
    public void Prefix_needs_minimum_length()
    {
        var installed = new[] { Entry("7z extra") };
        Assert.Null(InstalledAppsService.FindMatch("7", null, installed));
    }

    [Fact]
    public void Ambiguous_containment_returns_null()
    {
        // Both pass the overlap rule; without a publisher hint there is no
        // safe choice, so the result must be null.
        var installed = new[] { Entry("Firefox ESR"), Entry("Firefox Dev") };
        Assert.Null(InstalledAppsService.FindMatch("Firefox", null, installed));
    }

    [Fact]
    public void Publisher_disambiguates_ties()
    {
        var installed = new[]
        {
            Entry("Firefox ESR", "Mozilla"),
            Entry("Firefox Dev", "Contoso"),
        };
        var match = InstalledAppsService.FindMatch("Firefox", "Mozilla", installed);
        Assert.NotNull(match);
        Assert.Equal("Mozilla", match!.Info.Publisher);
    }

    [Fact]
    public void Weak_overlap_candidates_are_rejected_even_with_publisher()
    {
        // "Firefox Fork" contains "firefox" but fails the 60% overlap rule
        // against a much longer name, so only genuinely close names match.
        var installed = new[] { Entry("Mozilla Firefox Extended Support Release Edition", "Mozilla") };
        Assert.Null(InstalledAppsService.FindMatch("Firefox", "Mozilla", installed));
    }

    [Fact]
    public void Short_names_never_containment_match()
    {
        var installed = new[] { Entry("7zip") };
        Assert.Null(InstalledAppsService.FindMatch("7z", null, installed));
    }

    [Fact]
    public void Low_overlap_containment_rejected()
    {
        var installed = new[] { Entry("Microsoft Visual C++ 2015-2022 Redistributable (x64)") };
        Assert.Null(InstalledAppsService.FindMatch("visualc", null, installed));
    }
}
