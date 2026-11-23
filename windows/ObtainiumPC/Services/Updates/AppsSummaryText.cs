namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Apps-page subtitle summary. "All up to date" is claimed only when no
/// tracked app has an update AND no enabled app's last check failed: a
/// failed check means the app's true state is unknown, so the summary names
/// the failures instead of counting those apps as current. Disabled apps
/// are excluded from the failure count because they are deliberately not
/// checked (their row already says tracking is disabled); their stale
/// counter must not alarm the summary.
/// </summary>
public static class AppsSummaryText
{
    public static string Format(int total, int withUpdates, int withFailedChecks, Func<string, string> translate)
    {
        if (total <= 0) return "";
        if (withUpdates > 0 && withFailedChecks > 0)
            return string.Format(translate("{0} apps, {1} with updates, {2} with failed checks"), total, withUpdates, withFailedChecks);
        if (withUpdates > 0)
            return string.Format(translate("{0} apps, {1} with updates"), total, withUpdates);
        if (withFailedChecks > 0)
            return string.Format(translate("{0} apps, {1} with failed checks"), total, withFailedChecks);
        return string.Format(translate("{0} apps, all up to date"), total);
    }
}
