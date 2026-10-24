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
        var installed = new[] { Entry("Notepad++ (64-bit x64)"), Entry("Something Else") };
        var match = InstalledAppsService.FindMatch("Notepad++", null, installed);
        Assert.NotNull(match);
        Assert.Equal(InstalledAppsService.MatchConfidence.Strong, match!.Confidence);
    }

    [Fact]
    public void Ambiguous_containment_returns_null()
    {
        var installed = new[] { Entry("Mozilla Firefox (x64 en-US)"), Entry("Mozilla Firefox (x64 de)") };
        // Neither exact; both contain "firefox" with sufficient overlap.
        Assert.Null(InstalledAppsService.FindMatch("Firefox", null, installed));
    }

    [Fact]
    public void Publisher_disambiguates_ties()
    {
        var installed = new[]
        {
            Entry("Mozilla Firefox (x64 en-US)", "Mozilla"),
            Entry("Firefox Fork", "Someone Else"),
        };
        var match = InstalledAppsService.FindMatch("Firefox", "Mozilla", installed);
        Assert.NotNull(match);
        Assert.Equal("Mozilla", match!.Info.Publisher);
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
