using ObtainiumPC.Models;
using ObtainiumPC.Services.Updates;
using Xunit;
using static ObtainiumPC.Services.Updates.AppsSummaryText;

namespace ObtainiumPC.Tests;

public class AppsSummaryTextTests
{
    private static string Id(string s) => s;

    private static TrackedApp App(
        bool disabled = false,
        int failedChecks = 0,
        string? installed = "1.0.0",
        string? latest = "1.0.0",
        long? lastCheckUs = 1700000000_000_000L,
        string? additionalSettings = null)
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
            AdditionalSettingsJson = additionalSettings ?? "{}",
        };

    // --- Classifier: one test per distinct real state ---

    [Fact]
    public void New_unchecked_record_is_not_checked_yet()
    {
        // Freshly added app: no latest version, no recorded check, no failure.
        Assert.Equal(CheckState.NotCheckedYet, Classify(App(installed: null, latest: null, lastCheckUs: null), hasUpdate: false));
    }

    [Fact]
    public void Disabled_app_with_stale_failure_is_disabled_not_failed()
    {
        // A disabled app keeps its stale counter; it must not alarm the summary.
        Assert.Equal(CheckState.Disabled, Classify(App(disabled: true, failedChecks: 5), hasUpdate: false));
    }

    [Fact]
    public void Disabled_never_checked_app_is_disabled_not_unknown()
    {
        Assert.Equal(CheckState.Disabled, Classify(App(disabled: true, installed: null, latest: null, lastCheckUs: null), hasUpdate: false));
    }

    [Fact]
    public void Exempt_never_checked_app_is_not_checked_yet()
    {
        // exemptFromBackgroundUpdates skips background checks; classified by
        // actual state, so a never-checked exempt app is still unknown.
        Assert.Equal(CheckState.NotCheckedYet, Classify(
            App(installed: null, latest: null, lastCheckUs: null,
                additionalSettings: "{\"exemptFromBackgroundUpdates\":true}"),
            hasUpdate: false));
    }

    [Fact]
    public void Exempt_checked_app_is_current()
    {
        Assert.Equal(CheckState.Current, Classify(
            App(additionalSettings: "{\"exemptFromBackgroundUpdates\":true}"),
            hasUpdate: false));
    }

    [Fact]
    public void Not_installed_but_successfully_checked_is_current()
    {
        // Track-only app: nothing installed, latest known, check succeeded.
        Assert.Equal(CheckState.Current, Classify(App(installed: null, latest: "2.0.0"), hasUpdate: false));
    }

    [Fact]
    public void Stale_successful_check_stays_current()
    {
        // Periodic model: "current" means as of the last successful check.
        Assert.Equal(CheckState.Current, Classify(App(lastCheckUs: 1_600_000_000_000_000L), hasUpdate: false));
    }

    [Fact]
    public void Stale_failed_check_is_failed()
    {
        Assert.Equal(CheckState.FailedCheck, Classify(App(failedChecks: 3), hasUpdate: false));
    }

    [Fact]
    public void Update_available_is_update_available()
    {
        Assert.Equal(CheckState.UpdateAvailable, Classify(App(latest: "2.0.0"), hasUpdate: true));
    }

    [Fact]
    public void Failed_check_wins_over_update_available()
    {
        // Latest known version is stale when the newest check failed.
        Assert.Equal(CheckState.FailedCheck, Classify(App(failedChecks: 1, latest: "2.0.0"), hasUpdate: true));
    }

    // --- Format: every variant, including that unknown is never "current" ---

    [Fact]
    public void No_apps_is_empty()
    {
        Assert.Equal("", Format(0, 0, 0, 0, Id));
    }

    [Fact]
    public void All_current_claims_all_up_to_date()
    {
        Assert.Equal("3 apps, all up to date", Format(3, 0, 0, 0, Id));
    }

    [Fact]
    public void Updates_only()
    {
        Assert.Equal("3 apps, 1 with updates", Format(3, 1, 0, 0, Id));
    }

    [Fact]
    public void Failed_checks_are_named_not_counted_as_current()
    {
        Assert.Equal("2 apps, 1 with failed checks", Format(2, 0, 1, 0, Id));
    }

    [Fact]
    public void Never_checked_apps_are_named_not_counted_as_current()
    {
        Assert.Equal("2 apps, 1 not checked yet", Format(2, 0, 0, 1, Id));
    }

    [Fact]
    public void Updates_and_failed_checks_are_both_named()
    {
        Assert.Equal("4 apps, 1 with updates, 2 with failed checks", Format(4, 1, 2, 0, Id));
    }

    [Fact]
    public void Updates_and_not_checked_are_both_named()
    {
        Assert.Equal("4 apps, 1 with updates, 2 not checked yet", Format(4, 1, 0, 2, Id));
    }

    [Fact]
    public void Failed_checks_and_not_checked_are_both_named()
    {
        Assert.Equal("4 apps, 1 with failed checks, 2 not checked yet", Format(4, 0, 1, 2, Id));
    }

    [Fact]
    public void All_three_states_are_all_named()
    {
        Assert.Equal("6 apps, 1 with updates, 2 with failed checks, 3 not checked yet", Format(6, 1, 2, 3, Id));
    }

    [Fact]
    public void Translate_is_used_for_every_variant()
    {
        var seen = new List<string>();
        string T(string s) { seen.Add(s); return s; }
        Format(6, 1, 2, 3, T);
        Format(4, 1, 2, 0, T);
        Format(4, 1, 0, 2, T);
        Format(4, 0, 1, 2, T);
        Format(3, 1, 0, 0, T);
        Format(2, 0, 1, 0, T);
        Format(2, 0, 0, 1, T);
        Format(1, 0, 0, 0, T);
        Assert.Equal(new[]
        {
            "{0} apps, {1} with updates, {2} with failed checks, {3} not checked yet",
            "{0} apps, {1} with updates, {2} with failed checks",
            "{0} apps, {1} with updates, {2} not checked yet",
            "{0} apps, {1} with failed checks, {2} not checked yet",
            "{0} apps, {1} with updates",
            "{0} apps, {1} with failed checks",
            "{0} apps, {1} not checked yet",
            "{0} apps, all up to date",
        }, seen);
    }
}
