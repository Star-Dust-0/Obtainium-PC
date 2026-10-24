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
        _timer.Tick += async (_, _) => await CheckAllAsync(manual: false);
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

    public async Task CheckAllAsync(bool manual)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            await App.InitAsync();
            var updateableBefore = App.TrackedApps
                .Where(a => App.Updater.IsAppUpdateable(a))
                .Select(a => a.Id)
                .ToHashSet();
            foreach (var app in App.TrackedApps.Where(a => !a.Disabled).ToList())
            {
                if (!manual && app.GetSettingBool("exemptFromBackgroundUpdates")) continue;
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
                    Logging.AppLogger.Warn($"Update check failed for {app.FinalName} ({app.Url})", e);
                }
            }
            await App.SaveAppsAsync();
            LastCheck = DateTimeOffset.Now;
            LastUpdateCount = App.TrackedApps.Count(a => App.Updater.IsAppUpdateable(a));
            if (!manual && App.Settings.NotifyUpdates)
            {
                var newlyUpdateable = App.TrackedApps
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
        }
    }

    /// Windows toast for newly found updates. Unpackaged toast delivery is
    /// best-effort: if the platform refuses it, the in-app list still shows
    /// the updates.
    private static void NotifyUpdatesAvailable(List<TrackedApp> apps)
    {
        try
        {
            var text = apps.Count == 1
                ? $"Update available: {apps[0].FinalName} {apps[0].LatestVersion}"
                : $"{apps.Count} app updates available";
            var notification = new AppNotificationBuilder()
                .AddText("Obtainium-PC")
                .AddText(text)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toast unavailable (e.g. unpackaged identity limits); the in-app
            // list remains the source of truth.
        }
    }
}
