using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Apps-page subtitle summary and the per-app check-state classifier behind it.
///
/// "All up to date" is claimed only when EVERY visible app is known current:
/// no updates, no failed last check, and no app whose state is genuinely
/// unknown (never successfully checked). A failed check means the true state
/// is unknown, so failures are named; a never-checked app is named as not
/// checked yet instead of being counted as current. Disabled apps are
/// excluded from the failure/unknown counts because they are deliberately
/// not checked (their row already says tracking is disabled); they still
/// count toward the total. "Current" always means "as of the last successful
/// check": the app model is periodic checking, so a stale successful check
/// stays current until a check fails.
/// </summary>
public static class AppsSummaryText
{
    public enum CheckState { Current, UpdateAvailable, FailedCheck, NotCheckedYet, Disabled }

    /// <summary>
    /// Classify one app. hasUpdate is the row's existing update verdict
    /// (IsAppUpdateable), passed in so this stays pure and testable.
    /// FailedCheck wins over UpdateAvailable: when the latest check failed,
    /// the known "latest" is stale and the true state is uncertain. The
    /// exemptFromBackgroundUpdates flag is deliberately NOT read here: an
    /// exempt app is classified by its actual check state like any other.
    /// </summary>
    public static CheckState Classify(TrackedApp app, bool hasUpdate)
    {
        if (app.Disabled) return CheckState.Disabled;
        if (app.ConsecutiveFailedChecks > 0) return CheckState.FailedCheck;
        if (hasUpdate) return CheckState.UpdateAvailable;
        // Never successfully checked: no known latest version and no recorded
        // check. The state is genuinely unknown; never count it as current.
        if (app.LatestVersion == null && app.LastUpdateCheckUs == null) return CheckState.NotCheckedYet;
        return CheckState.Current;
    }

    public static string Format(int total, int withUpdates, int withFailedChecks, int notCheckedYet, Func<string, string> translate)
    {
        if (total <= 0) return "";
        var u = withUpdates > 0;
        var f = withFailedChecks > 0;
        var n = notCheckedYet > 0;
        if (u && f && n)
            return string.Format(translate("{0} apps, {1} with updates, {2} with failed checks, {3} not checked yet"), total, withUpdates, withFailedChecks, notCheckedYet);
        if (u && f)
            return string.Format(translate("{0} apps, {1} with updates, {2} with failed checks"), total, withUpdates, withFailedChecks);
        if (u && n)
            return string.Format(translate("{0} apps, {1} with updates, {2} not checked yet"), total, withUpdates, notCheckedYet);
        if (f && n)
            return string.Format(translate("{0} apps, {1} with failed checks, {2} not checked yet"), total, withFailedChecks, notCheckedYet);
        if (u)
            return string.Format(translate("{0} apps, {1} with updates"), total, withUpdates);
        if (f)
            return string.Format(translate("{0} apps, {1} with failed checks"), total, withFailedChecks);
        if (n)
            return string.Format(translate("{0} apps, {1} not checked yet"), total, notCheckedYet);
        return string.Format(translate("{0} apps, all up to date"), total);
    }
}
