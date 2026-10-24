using System.Text.Json;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Settings;

namespace ObtainiumPC.Services.Updates;

public enum UpdateHealthReason
{
    StaleRelease,
    StaleVersion,
    FailedChecks,
    Archived,
    FilteredReleases,
}

public sealed record UpdateHealthConfig(
    int? StaleReleaseDays,
    int? StaleVersionDays,
    int? FailedChecksCount,
    bool ArchivedEnabled,
    int? FilteredReleasesCount);

/// <summary>
/// Port of lib/utils/update_health.dart. Global thresholds live in AppSettings;
/// per-app overrides live in additionalSettings as '' (use global), 'off', or a
/// day/count string.
/// </summary>
public static class UpdateHealthService
{
    public static readonly IReadOnlyList<int> DayOptions = new[] { 0, 30, 60, 90, 180, 365, 730 };
    public static readonly IReadOnlyList<int> FailedChecksOptions = new[] { 0, 1, 2, 3, 5, 8, 10, 15, 20 };

    private static int? ResolveThreshold(IReadOnlyDictionary<string, JsonElement> settings, string key, int globalValue)
    {
        if (settings.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var value = el.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                if (value == "off") return null;
                if (int.TryParse(value, out var parsed)) return parsed < 1 ? null : parsed;
            }
        }
        return globalValue < 1 ? null : globalValue;
    }

    private static bool ResolveArchived(IReadOnlyDictionary<string, JsonElement> settings, bool globalEnabled)
    {
        if (settings.TryGetValue("updateHealthArchived", out var el) && el.ValueKind == JsonValueKind.String)
        {
            var value = el.GetString();
            if (value == "off") return false;
            if (value == "on") return true;
        }
        return globalEnabled;
    }

    public static UpdateHealthConfig ResolveConfig(IReadOnlyDictionary<string, JsonElement> additionalSettings, AppSettings settings) =>
        new(
            ResolveThreshold(additionalSettings, "updateHealthStaleRelease", settings.UpdateHealthStaleReleaseDays),
            ResolveThreshold(additionalSettings, "updateHealthStaleVersion", settings.UpdateHealthStaleVersionDays),
            ResolveThreshold(additionalSettings, "updateHealthFailedChecks", settings.UpdateHealthFailedChecksCount),
            ResolveArchived(additionalSettings, settings.UpdateHealthArchivedEnabled),
            ResolveThreshold(additionalSettings, "updateHealthFilteredReleases", settings.UpdateHealthFilteredReleasesCount));

    public static bool IsIgnored(TrackedApp app) =>
        !app.UpdateHealthEnabled ||
        (app.UpdateHealthDismissedVersion != null && app.UpdateHealthDismissedVersion == app.LatestVersion);

    public static List<UpdateHealthReason> EvaluateReasons(TrackedApp app, UpdateHealthConfig config, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.Now;
        var reasons = new List<UpdateHealthReason>();

        if (config.StaleReleaseDays is { } staleReleaseDays && app.ReleaseDateUs is { } releaseDateUs)
        {
            var releaseDate = DateTimeOffset.FromUnixTimeMilliseconds(releaseDateUs / 1000);
            if ((currentTime - releaseDate).Days >= staleReleaseDays) reasons.Add(UpdateHealthReason.StaleRelease);
        }
        if (config.StaleVersionDays is { } staleVersionDays && app.LatestVersionChangedAtUs is { } changedAtUs)
        {
            var changedAt = DateTimeOffset.FromUnixTimeMilliseconds(changedAtUs / 1000);
            if ((currentTime - changedAt).Days >= staleVersionDays) reasons.Add(UpdateHealthReason.StaleVersion);
        }
        if (config.FailedChecksCount is { } failedChecks && app.ConsecutiveFailedChecks >= failedChecks)
            reasons.Add(UpdateHealthReason.FailedChecks);
        if (config.ArchivedEnabled && app.IsArchived == true)
            reasons.Add(UpdateHealthReason.Archived);
        if (config.FilteredReleasesCount is { } filteredCount &&
            app.NewerReleasesFilteredOut is { } filteredOut && filteredOut >= filteredCount)
            reasons.Add(UpdateHealthReason.FilteredReleases);

        return reasons;
    }

    public static bool HasIssues(TrackedApp app, AppSettings settings)
    {
        if (app.Disabled || app.HasPendingRepoRename || IsIgnored(app)) return false;
        return EvaluateReasons(app, ResolveConfig(app.AdditionalSettings, settings)).Count > 0;
    }

    public static string ReasonLabel(UpdateHealthReason reason, TrackedApp app, UpdateHealthConfig config) => reason switch
    {
        UpdateHealthReason.StaleRelease => $"Latest release is older than {config.StaleReleaseDays} days",
        UpdateHealthReason.StaleVersion => $"Version has not changed in {config.StaleVersionDays} days",
        UpdateHealthReason.FailedChecks => $"{app.ConsecutiveFailedChecks} consecutive failed update checks",
        UpdateHealthReason.Archived => "Repository is archived",
        UpdateHealthReason.FilteredReleases => $"{app.NewerReleasesFilteredOut ?? 0} newer releases filtered out",
        _ => reason.ToString(),
    };

    public static string DaysLabel(int days) => days < 1 ? "Disabled" : $"{days} days";
    public static string CountLabel(int count, string unit) => count < 1 ? "Disabled" : $"{count} {unit}";

    /// Network/availability errors should not count as app-level failures
    /// (matches upstream shouldCountFailedCheck).
    public static bool ShouldCountFailedCheck(Exception error) =>
        error is not RateLimitException && error is not HttpRequestException;
}

public sealed class RateLimitException : Exception
{
    public RateLimitException(string message) : base(message) { }
}
