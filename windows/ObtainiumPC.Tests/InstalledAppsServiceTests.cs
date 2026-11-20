using System.Collections.Generic;
using System.Linq;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

[Collection("InstalledAppsCache")]
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

    [Fact]
    public void Repository_name_with_plus_signs_matches_registry_name()
    {
        // Real runner evidence: tracked "notepad-plus-plus" vs registry "Notepad++ (64-bit x64)".
        var installed = new[] { Entry("Notepad++ (64-bit x64)", "Notepad++ Team", "8.9.8.1"), Entry("Other App") };
        var match = InstalledAppsService.FindMatch("notepad-plus-plus", "notepad-plus-plus", installed);
        Assert.NotNull(match);
        Assert.Equal("8.9.8.1", match!.Info.DisplayVersion);
    }

    [Fact]
    public void Program_named_after_owner_matches_when_unique()
    {
        // Real runner evidence: tracked "Bulk-Crap-Uninstaller" by owner "BCUninstaller" vs "BCUninstaller 6.3.0.0".
        var installed = new[] { Entry("BCUninstaller 6.3.0.0", "Marcin Szeniak", "6.3.0.0"), Entry("Other App") };
        var match = InstalledAppsService.FindMatch("Bulk-Crap-Uninstaller", "BCUninstaller", installed);
        Assert.NotNull(match);
        Assert.Equal("6.3.0.0", match!.Info.DisplayVersion);
    }

    [Fact]
    public void Owner_tier_is_ambiguity_safe_and_exact_stem_only()
    {
        var two = new[] { Entry("BCUninstaller 6.3.0.0"), Entry("BCUninstaller 5.0") };
        Assert.Null(InstalledAppsService.FindMatch("Bulk-Crap-Uninstaller", "BCUninstaller", two));
        var unrelated = new[] { Entry("BCUninstaller Extras Pack 1") };
        Assert.Null(InstalledAppsService.FindMatch("Bulk-Crap-Uninstaller", "BCUninstaller", unrelated));
        Assert.Null(InstalledAppsService.FindMatch("Some-Tool", "Acme", new[] { Entry("Acme 2.0") }));
    }

    [Theory]
    [InlineData("Security Update for Microsoft Word (KB5002345)", null, null, true)]
    [InlineData("Update for Windows (KB5001234)", null, null, true)]
    [InlineData("Hotfix for SQL Server", null, null, true)]
    [InlineData("Some Patch", "Update", null, true)]
    [InlineData("Some Patch", null, "Microsoft.Office", true)]
    [InlineData("Notepad++ (64-bit x64)", null, null, false)]
    [InlineData("Updater Tool", null, null, false)]
    public void Windows_updates_are_not_programs(string name, string? release, string? parent, bool isUpdate)
        => Assert.Equal(isUpdate, InstalledAppsService.IsUpdateEntry(name, release, parent));

    [Fact]
    public void The_same_program_in_two_registry_views_is_one_program_and_matches_exactly()
    {
        var a = Entry("Shuck", "Acme", "1.0.1");
        var all = InstalledAppsService.Dedupe(new[] { a, Entry("Shuck", "Acme", "1.0.1"), Entry("Other", "X") });
        Assert.Equal(2, all.Count);
        Assert.NotNull(InstalledAppsService.FindMatch("Shuck", null, all));
        // Two different versions of the same name stay separate (genuinely ambiguous).
        Assert.Equal(2, InstalledAppsService.Dedupe(new[] { Entry("Shuck", "Acme", "1.0"), Entry("Shuck", "Acme", "2.0") }).Count);
    }

    [Fact]
    public void Notepad_does_not_match_Notepad_plus_plus()
    {
        var installed = new[] { Entry("Notepad++ (64-bit x64)", "Notepad++ Team", "8.9.8.1"), Entry("Other App") };
        Assert.Null(InstalledAppsService.FindMatch("Notepad", null, installed));
        Assert.NotNull(InstalledAppsService.FindMatch("Notepad++", null, installed));
    }

    [Fact]
    public void A_remembered_uninstall_key_wins_over_a_renamed_display_name()
    {
        var app = new ObtainiumPC.Models.TrackedApp { Name = "Tool", Author = "x" };
        app.SetSetting(InstalledAppsService.InstalledKeySetting, "ToolKey_1");
        var installed = new[] { new InstalledAppInfo("Completely Renamed Product", "2.0", null, null, "ToolKey_1"), Entry("Other") };
        var m = InstalledAppsService.FindMatchFor(app, installed);
        Assert.Equal(InstalledAppsService.MatchConfidence.Exact, m!.Confidence);
        Assert.Equal("2.0", m.Info.DisplayVersion);
    }

    [Fact]
    public void Manual_detect_sees_an_install_that_finished_after_the_cache_was_filled()
    {
        var app = new ObtainiumPC.Models.TrackedApp { Name = "Shuck", Author = "acme" };
        var data = new List<InstalledAppInfo> { new("Shuck", "1.0.0", null, "acme") };
        InstalledAppsService.ReaderOverride = () => data.ToList();
        try
        {
            InstalledAppsService.InvalidateCache();
            // The watcher scans: the cache now holds the pre-install state.
            Assert.Equal("1.0.0", InstalledAppsService.FindMatchFor(app)!.Info.DisplayVersion);
            // The installer finishes within the cache lifetime.
            data[0] = new("Shuck", "2.0.0", null, "acme");
            // Documents the cause: a plain match inside the cache lifetime still returns the old version.
            Assert.Equal("1.0.0", InstalledAppsService.FindMatchFor(app)!.Info.DisplayVersion);
            // The manual refresh path must see the new version.
            Assert.Equal("2.0.0", InstalledAppsService.RefreshAndFindMatchFor(app)!.Info.DisplayVersion);
        }
        finally { InstalledAppsService.ReaderOverride = null; InstalledAppsService.InvalidateCache(); }
    }
}

[CollectionDefinition("InstalledAppsCache", DisableParallelization = true)]
public class InstalledAppsCacheCollection { }
