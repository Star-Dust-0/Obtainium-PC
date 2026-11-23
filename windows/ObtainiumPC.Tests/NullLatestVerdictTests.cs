using System;
using System.IO;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Updates;
using ObtainiumPC.Services.Versioning;
using Xunit;
using static ObtainiumPC.Services.Updates.AppsSummaryText;

namespace ObtainiumPC.Tests;

/// Null-latest invariant: no update verdict without a known latest version.
/// Before the fix, IsAppUpdateable(installed, null, hideDowngrades:false) returned true
/// ("different from installed"), so installed never-checked / version-unknown rows were
/// presented as "Update available" and Classify's NotCheckedYet / UnknownVersion states
/// were unreachable for installed rows. With hideDowngrades on, the same call reached
/// CompareVersionsNumerically(installed, null) -> a null-keyed Dictionary lookup
/// (ArgumentNullException). These tests compose the REAL verdict (SelfUpdate.IsUpdateable,
/// the same function the app's row building uses) into the summary classifier.
public class NullLatestVerdictTests
{
    private static TrackedApp App(
        bool disabled = false,
        int failedChecks = 0,
        string? installed = "1.0.0",
        string? latest = "1.0.0",
        long? lastCheckUs = 1700000000_000_000L)
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            Url = "https://github.com/example/app",
            Name = "Example",
            Disabled = disabled,
            ConsecutiveFailedChecks = failedChecks,
            LastCheckError = failedChecks > 0 ? "http_404" : null,
            InstalledVersion = installed,
            LatestVersion = latest,
            LastUpdateCheckUs = lastCheckUs,
            AdditionalSettingsJson = "{}",
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Null_latest_is_never_an_update(bool hide)
    {
        // Also the no-throw regression: pre-fix, hide=true dereferenced null here.
        Assert.False(VersionService.IsAppUpdateable("1.0", null, hide));
        Assert.False(VersionService.IsAppUpdateable(null, null, hide));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Installed_never_checked_classifies_not_checked_yet(bool hide)
    {
        var app = App(installed: "1.0", latest: null, lastCheckUs: null);
        Assert.Equal(CheckState.NotCheckedYet, Classify(app, SelfUpdate.IsUpdateable(app, hide)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Installed_checked_but_version_unknown_classifies_unknown(bool hide)
    {
        // e.g. minimumUpdateAgeDays suppressed the release: check succeeded, latest stays null.
        var app = App(installed: "1.0", latest: null);
        Assert.Equal(CheckState.UnknownVersion, Classify(app, SelfUpdate.IsUpdateable(app, hide)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Installed_failing_with_null_latest_classifies_failed(bool hide)
    {
        var app = App(installed: "1.0", latest: null, failedChecks: 2);
        Assert.Equal(CheckState.FailedCheck, Classify(app, SelfUpdate.IsUpdateable(app, hide)));
    }

    [Fact]
    public void Track_only_never_checked_classifies_not_checked_yet()
    {
        var app = App(installed: null, latest: null, lastCheckUs: null);
        Assert.Equal(CheckState.NotCheckedYet, Classify(app, SelfUpdate.IsUpdateable(app, false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_check_wins_over_stale_known_latest(bool hide)
    {
        // Latest known from an older successful check, current check failing:
        // the stale update must not be presented as a fresh verdict.
        var app = App(installed: "1.0", latest: "2.0", failedChecks: 3);
        Assert.True(SelfUpdate.IsUpdateable(app, hide)); // stale latest still yields the verdict
        Assert.Equal(CheckState.FailedCheck, Classify(app, SelfUpdate.IsUpdateable(app, hide)));
    }

    [Fact]
    public void Stale_known_latest_with_successful_check_still_offers_the_update()
    {
        var app = App(installed: "1.0", latest: "2.0");
        Assert.True(SelfUpdate.IsUpdateable(app, false));
        Assert.Equal(CheckState.UpdateAvailable, Classify(app, SelfUpdate.IsUpdateable(app, false)));
    }

    // AppsPage.xaml.cs is UI-bound and not compiled into the test assembly, so the
    // row/summary consistency rule is guarded at source level (project convention,
    // see ArabicOverlayTests): the row Status getter must name a failed check before
    // it ever presents "Update available", matching Classify's FailedCheck-wins rule.
    [Fact]
    public void Row_status_names_failure_before_any_update_verdict()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ObtainiumPC", "Pages", "AppsPage.xaml.cs"))) d = d.Parent;
        var src = File.ReadAllText(Path.Combine(d!.FullName, "ObtainiumPC", "Pages", "AppsPage.xaml.cs"));
        var getter = src.IndexOf("public string Status", StringComparison.Ordinal);
        var failed = src.IndexOf("ConsecutiveFailedChecks > 0", getter, StringComparison.Ordinal);
        var update = src.IndexOf("HasUpdate) return", getter, StringComparison.Ordinal);
        Assert.True(getter > 0 && failed > getter && update > failed,
            "row Status must check ConsecutiveFailedChecks before HasUpdate");
    }
}
