namespace ObtainiumPC.Services.Updates;

/// <summary>Decides whether a background update check should be skipped. A manual check never is.</summary>
public static class BackgroundCheckGuards
{
    /// <returns>A short reason when the check is skipped, otherwise null.</returns>
    public static string? SkipReason(bool manual, bool skipOnMetered, bool skipOnBattery, bool? isMetered, bool? onBattery)
    {
        if (manual) return null;
        // Unknown (null) never blocks: a guard only applies on a positively detected condition.
        if (skipOnMetered && isMetered == true) return "metered connection";
        if (skipOnBattery && onBattery == true) return "battery power";
        return null;
    }
}
