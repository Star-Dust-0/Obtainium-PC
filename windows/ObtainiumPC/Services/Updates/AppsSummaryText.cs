using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Apps-page subtitle summary and the per-app check-state classifier behind it.
///
/// Truth rules:
/// - "All up to date" is claimed only when EVERY visible app is enabled,
///   checked-current AND has an installed version that can actually be up to
///   date. Track-only (no installed version), disabled, failed, never-checked
///   and unknown-version entries never create that claim; with no problems to
///   name the subtitle falls back to a neutral "N apps".
/// - A failed check means the true state is unknown, so failures are named.
/// - A never-checked app (no latest version, no recorded check) is named as
///   not checked yet, never counted as current.
/// - A successfully checked app whose latest version is still unknown (for
///   example a release suppressed by minimumUpdateAgeDays, where the check
///   succeeds but LatestVersion deliberately stays null) is named as unknown
///   status: it is neither current nor never-checked.
/// - "Current" means "as of the last successful check" (the app model is
///   periodic checking); it is never a proof of live currency.
/// - Disabled apps are deliberately not checked: excluded from every count,
///   and their presence blocks the blanket claim (they are not "current").
/// - The exemptFromBackgroundUpdates flag is deliberately NOT read here: an
///   exempt app is classified by its actual check state like any other.
/// </summary>
public static class AppsSummaryText
{
    public enum CheckState { Current, UpdateAvailable, FailedCheck, NotCheckedYet, UnknownVersion, Disabled }

    /// <summary>
    /// Classify one app. hasUpdate is the row's existing update verdict
    /// (IsAppUpdateable), passed in so this stays pure and testable.
    /// FailedCheck wins over UpdateAvailable: when the latest check failed,
    /// the known "latest" is stale and the true state is uncertain.
    /// </summary>
    public static CheckState Classify(TrackedApp app, bool hasUpdate)
    {
        if (app.Disabled) return CheckState.Disabled;
        if (app.ConsecutiveFailedChecks > 0) return CheckState.FailedCheck;
        if (hasUpdate) return CheckState.UpdateAvailable;
        if (app.LatestVersion == null)
            // Never checked vs checked-but-version-unknown (e.g. a release
            // suppressed by minimumUpdateAgeDays): two distinct honest states.
            return app.LastUpdateCheckUs == null ? CheckState.NotCheckedYet : CheckState.UnknownVersion;
        return CheckState.Current;
    }

    /// <summary>
    /// Whether one visible row may participate in an "all up to date" claim:
    /// checked-current AND an installed version that can actually be current.
    /// Track-only, disabled, failed, never-checked and unknown-version rows
    /// never create the claim.
    /// </summary>
    public static bool IsClaimable(CheckState state, bool hasInstalledVersion) =>
        state == CheckState.Current && hasInstalledVersion;

    public static string Format(int total, int withUpdates, int withFailedChecks, int notCheckedYet, int unknownVersion, bool claimUpToDate, Func<string, string> translate)
    {
        if (total <= 0) return "";
        if (claimUpToDate)
            return string.Format(translate("{0} apps, all up to date"), total);
        if (withUpdates > 0 && withFailedChecks == 0 && notCheckedYet == 0 && unknownVersion == 0)
            // Preserve the established localized key for the common case.
            return string.Format(translate("{0} apps, {1} with updates"), total, withUpdates);
        var parts = new List<string>(4);
        if (withUpdates > 0) parts.Add(string.Format(translate("{0} with updates"), withUpdates));
        if (withFailedChecks > 0) parts.Add(string.Format(translate("{0} with failed checks"), withFailedChecks));
        if (notCheckedYet > 0) parts.Add(string.Format(translate("{0} not checked yet"), notCheckedYet));
        if (unknownVersion > 0) parts.Add(string.Format(translate("{0} with unknown status"), unknownVersion));
        if (parts.Count == 0)
            // Nothing wrong to name, but not everything is installed and
            // current (track-only/disabled present): stay neutral.
            return string.Format(translate("{0} apps"), total);
        return string.Format(translate("{0} apps: {1}"), total, string.Join(translate(", "), parts));
    }
}
