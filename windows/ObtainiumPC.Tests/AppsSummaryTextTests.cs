using System.Collections.Generic;
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
    public void Successful_check_with_suppressed_release_is_unknown_not_current()
    {
        // minimumUpdateAgeDays path: the check SUCCEEDS (time recorded,
        // counter reset) but LatestVersion deliberately stays null. This must
        // be called neither current nor never-checked.
        Assert.Equal(CheckState.UnknownVersion, Classify(App(latest: null, lastCheckUs: 1700000000_000_000L), hasUpdate: false));
    }

    [Fact]
    public void Disabled_app_with_stale_failure_is_disabled_not_failed()
    {
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
    public void Not_installed_but_successfully_checked_is_current_state()
    {
        // Track-only app: check succeeded, latest known - but with nothing
        // installed it can never create an "all up to date" claim (below).
        Assert.Equal(CheckState.Current, Classify(App(installed: null, latest: "2.0.0"), hasUpdate: false));
    }

    [Fact]
    public void Stale_successful_check_stays_current_as_of_last_check()
    {
        // Periodic model: "current" means as of the last successful check,
        // never a proof of live currency.
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
        Assert.Equal(CheckState.FailedCheck, Classify(App(failedChecks: 1, latest: "2.0.0"), hasUpdate: true));
    }

    // --- Claim rule: only checked-current AND installed rows create it ---

    [Fact]
    public void Claim_rule_matrix()
    {
        Assert.True(IsClaimable(CheckState.Current, hasInstalledVersion: true));
        Assert.False(IsClaimable(CheckState.Current, hasInstalledVersion: false)); // track-only
        Assert.False(IsClaimable(CheckState.Disabled, hasInstalledVersion: true));
        Assert.False(IsClaimable(CheckState.Disabled, hasInstalledVersion: false));
        Assert.False(IsClaimable(CheckState.FailedCheck, hasInstalledVersion: true));
        Assert.False(IsClaimable(CheckState.NotCheckedYet, hasInstalledVersion: false));
        Assert.False(IsClaimable(CheckState.UnknownVersion, hasInstalledVersion: true));
        Assert.False(IsClaimable(CheckState.UpdateAvailable, hasInstalledVersion: true));
    }

    // --- Format: variants and honest fallbacks ---

    [Fact]
    public void No_apps_is_empty()
    {
        Assert.Equal("", Format(0, 0, 0, 0, 0, false, Id));
    }

    [Fact]
    public void All_current_and_installed_claims_all_up_to_date()
    {
        Assert.Equal("3 apps, all up to date", Format(3, 0, 0, 0, 0, true, Id));
    }

    [Fact]
    public void No_problems_but_not_claimable_stays_neutral()
    {
        // e.g. only track-only or disabled rows visible: no blanket claim.
        Assert.Equal("2 apps", Format(2, 0, 0, 0, 0, false, Id));
    }

    [Fact]
    public void Updates_only_keeps_the_established_key()
    {
        Assert.Equal("3 apps, 1 with updates", Format(3, 1, 0, 0, 0, false, Id));
    }

    [Fact]
    public void Failed_checks_are_named_not_counted_as_current()
    {
        Assert.Equal("2 apps: 1 with failed checks", Format(2, 0, 1, 0, 0, false, Id));
    }

    [Fact]
    public void Never_checked_apps_are_named_not_counted_as_current()
    {
        Assert.Equal("2 apps: 1 not checked yet", Format(2, 0, 0, 1, 0, false, Id));
    }

    [Fact]
    public void Unknown_version_is_named_as_unknown()
    {
        Assert.Equal("2 apps: 1 with unknown status", Format(2, 0, 0, 0, 1, false, Id));
    }

    [Fact]
    public void Mixed_states_are_all_named()
    {
        Assert.Equal("7 apps: 1 with updates, 2 with failed checks, 3 not checked yet, 1 with unknown status",
            Format(7, 1, 2, 3, 1, false, Id));
    }

    [Fact]
    public void Translate_is_used_for_every_piece()
    {
        var seen = new List<string>();
        string T(string s) { seen.Add(s); return s; }
        Format(1, 0, 0, 0, 0, true, T);       // all up to date
        Format(3, 1, 0, 0, 0, false, T);      // established updates key
        Format(2, 0, 0, 0, 0, false, T);      // neutral
        Format(7, 1, 2, 3, 1, false, T);      // composed
        Assert.Equal(new[]
        {
            "{0} apps, all up to date",
            "{0} apps, {1} with updates",
            "{0} apps",
            "{0} with updates",
            "{0} with failed checks",
            "{0} not checked yet",
            "{0} with unknown status",
            "{0} apps: {1}",
            ", ",
        }, seen);
    }

    [Fact]
    public void Composed_parts_and_separator_come_from_the_translator()
    {
        var ar = new Dictionary<string, string>
        {
            ["{0} apps: {1}"] = "{0} APPS: {1}",
            ["{0} with updates"] = "{0} UPD",
            ["{0} with failed checks"] = "{0} FAILED",
            [", "] = " / ",
        };
        Assert.Equal("3 APPS: 1 UPD / 2 FAILED", Format(3, 1, 2, 0, 0, false, s => ar[s]));
    }
}
