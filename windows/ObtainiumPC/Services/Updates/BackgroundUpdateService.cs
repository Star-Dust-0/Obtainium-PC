using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Periodic background update checking. Mirrors the original's background
/// update pass: apps marked exemptFromBackgroundUpdates are skipped here (a
/// manual refresh still checks them), failures increment
/// consecutiveFailedChecks (feeding update health), and listeners are told
/// when the app list changed.
/// </summary>
public sealed class BackgroundUpdateService
{
    private static BackgroundUpdateService? _instance;
    public static BackgroundUpdateService Instance => _instance ??= new BackgroundUpdateService();

    private readonly DispatcherQueueTimer _timer;
    private bool _checking;
    // Only one check run at a time touches the tracked-app objects (full check, or a deep-link refresh).
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public event EventHandler? AppsUpdated;

    /// Raised by mutators outside update checks (deep-link imports, manual
    /// refreshes) so open pages reload. AppsUpdated stays raisable only here.
    public void NotifyAppsChanged() => AppsUpdated?.Invoke(this, EventArgs.Empty);
    public DateTimeOffset? LastCheck { get; private set; }
    public int LastUpdateCount { get; private set; }
    public bool IsChecking => _checking;

    private BackgroundUpdateService()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += async (_, _) =>
        {
            try { await CheckAllAsync(manual: false); }
            catch (Exception ex) { Logging.AppLogger.Warn("periodic update check failed", ex); }
        };
    }

    /// Starts the interval timer (and an immediate check when checkOnStart is set).
    /// Must be called on the UI thread after App.InitAsync().
    public void Start()
    {
        if (_started) return;
        _started = true;
        _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(App.Settings.UpdateIntervalMinutes, 15, 10080));
        _timer.Start();
        if (App.Settings.CheckOnStart) _ = CheckAllAsync(manual: false);
    }

    /// Applies a changed check interval right away instead of at the next start.
    public void ApplyInterval()
    {
        if (!_started) return;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMinutes(Math.Clamp(App.Settings.UpdateIntervalMinutes, 15, 10080));
        _timer.Start();
    }

    private static bool? DetectMetered()
    {
        try
        {
            var cost = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            if (cost == null) return null;
            return cost.NetworkCostType != Windows.Networking.Connectivity.NetworkCostType.Unrestricted
                && cost.NetworkCostType != Windows.Networking.Connectivity.NetworkCostType.Unknown;
        }
        catch { return null; }
    }

    private static bool? DetectOnBattery()
    {
        try
        {
            // No battery present (desktop) reports NotPresent: not on battery.
            return Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.Discharging;
        }
        catch { return null; }
    }

    public async Task CheckAllAsync(bool manual)
    {
        if (_checking) return;
        var guard = BackgroundCheckGuards.SkipReason(manual,
            App.Settings.SkipBackgroundChecksOnMeteredConnection, App.Settings.SkipBackgroundChecksOnBattery,
            App.Settings.SkipBackgroundChecksOnMeteredConnection ? DetectMetered() : null,
            App.Settings.SkipBackgroundChecksOnBattery ? DetectOnBattery() : null);
        if (guard != null)
        {
            Logging.AppLogger.Info($"Background update check skipped: {guard}");
            return;
        }
        if (!await _gate.WaitAsync(0)) return;
        _checking = true;
        try
        {
            await App.InitAsync();
            var updateableBefore = App.SnapshotApps()
                .Where(a => App.Updater.IsAppUpdateable(a))
                .Select(a => a.Id)
                .ToHashSet();
            foreach (var app in App.SnapshotApps().Where(a => !a.Disabled).ToList())
            {
                if (!manual && app.GetSettingBool("exemptFromBackgroundUpdates")) continue;
                // lib/providers/apps_provider_updates.dart getAppsSortedByUpdateCheckTime:
                // apps in a category excluded from checks are skipped.
                if (App.Settings.IsExcludedFromChecks(app.Categories)) continue;
                // lib/providers/apps_provider_updates.dart: when
                // onlyCheckInstalledOrTrackOnlyApps is on, checks are limited
                // to installed or track-only apps.
                if (App.Settings.OnlyCheckInstalledOrTrackOnlyApps
                    && app.InstalledVersion == null
                    && !app.GetSettingBool("trackOnly")) continue;
                // Windows-only: legacy Android records stay in the store untouched but are not checked.
                if (App.Updater.IsLegacyUnsupported(app))
                {
                    Logging.AppLogger.Info($"Skipped update check for legacy Android record {app.FinalName} ({app.Url})");
                    continue;
                }
                try
                {
                    await App.Updater.CheckAppAsync(app.Url, app.AdditionalSettings, app);
                }
                // lib/providers/apps_provider_updates.dart: a detected repo
                // rename is not an error; flag it and leave the app unchanged.
                catch (Sources.RepositoryRenamedException rre)
                {
                    app.PendingRepoRenameUrl = rre.NewUrl;
                    Logging.AppLogger.Info($"Repo rename detected for {app.FinalName}: {rre.NewUrl}");
                }
                catch (Exception e)
                {
                    app.ConsecutiveFailedChecks++;
                    // Persist ONLY an allowlisted category - never raw message text. The Apps
                    // page renders the localized reason from the category.
                    app.LastCheckError = CheckFailureText.CategoryFor(e);
                    Logging.AppLogger.Warn($"Update check failed for {app.FinalName} ({app.Url})", e);
                }
            }
            await App.SaveAppsAsync();
            LastCheck = DateTimeOffset.Now;
            LastUpdateCount = App.SnapshotApps().Count(a => App.Updater.IsAppUpdateable(a));
            if (!manual && App.Settings.NotifyUpdates)
            {
                var newlyUpdateable = App.SnapshotApps()
                    .Where(a => !updateableBefore.Contains(a.Id)
                        && App.Updater.IsAppUpdateable(a)
                        && !a.GetSettingBool("skipUpdateNotifications"))
                    .ToList();
                if (newlyUpdateable.Count > 0) NotifyUpdatesAvailable(newlyUpdateable);
            }
            AppsUpdated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _checking = false;
            _gate.Release();
        }
    }

    /// Checks one app by id (deep-link refresh). Waits for any running check, then looks the app up in
    /// the current list so a replaced or removed entry is never checked through a stale object.
    public async Task CheckOneAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            await App.InitAsync();
            var app = App.SnapshotApps().FirstOrDefault(a => a.Id == id);
            if (app == null) return;
            await App.Updater.CheckAppAsync(app.Url, app.AdditionalSettings, app);
            await App.SaveAppsAsync();
        }
        finally { _gate.Release(); }
    }

    /// Windows toast for newly found updates. Unpackaged toast delivery is
    /// best-effort: if the platform refuses it, the in-app list still shows
    /// the updates.
    private static void NotifyUpdatesAvailable(List<TrackedApp> apps)
    {
        try
        {
            var text = apps.Count == 1
                ? string.Format(Localization.Loc.T("Update available: {0} {1}"), apps[0].FinalName, apps[0].LatestVersion)
                : string.Format(Localization.Loc.T("{0} app updates available"), apps.Count);
            var notification = new AppNotificationBuilder()
                .AddText("Obtainium-PC")
                .AddText(text)
                .BuildNotification();
            Notifications.Notifier.Show(notification, "update available");
        }
        catch (Exception ex)
        {
            // The in-app list remains the source of truth; the failure is logged now.
            Logging.AppLogger.Warn("Update notification could not be built", ex);
        }
    }
}
