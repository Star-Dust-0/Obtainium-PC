using Microsoft.UI.Dispatching;
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
            foreach (var app in App.TrackedApps.Where(a => !a.Disabled).ToList())
            {
                if (!manual && app.GetSettingBool("exemptFromBackgroundUpdates")) continue;
                try
                {
                    await App.Updater.CheckAppAsync(app.Url, app.AdditionalSettings, app);
                }
                catch
                {
                    app.ConsecutiveFailedChecks++;
                }
            }
            await App.SaveAppsAsync();
            LastCheck = DateTimeOffset.Now;
            LastUpdateCount = App.TrackedApps.Count(a => App.Updater.IsAppUpdateable(a));
            AppsUpdated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _checking = false;
        }
    }
}
