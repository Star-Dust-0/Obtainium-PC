using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ObtainiumPC.Services.Download;
using ObtainiumPC.Services.Localization;
using ObtainiumPC.Services.Logging;

namespace ObtainiumPC.Services.Notifications;

/// <summary>
/// Native Windows notifications. Registration and every failure are logged
/// (they used to be swallowed). Support is NOT assumed: whether a toast is
/// actually shown depends on the desktop, so the result of each Register/Show
/// is recorded in the app log and nothing here claims delivery.
/// </summary>
public static class Notifier
{
    static DispatcherQueue? _ui;
    static bool _registered;
    static readonly HashSet<string> Tracked = new();

    public static bool Registered => _registered;

    public static void Initialize(DispatcherQueue ui)
    {
        _ui = ui;
        try
        {
            var mgr = AppNotificationManager.Default;
            mgr.NotificationInvoked += OnInvoked;
            mgr.Register();
            _registered = true;
            AppLogger.Info($"Notifications registered (IsSupported={AppNotificationManager.IsSupported()})");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Notification registration failed; notifications may not appear", ex);
        }
        DownloadRegistry.ListChanged += OnListChanged;
    }

    public static void Shutdown()
    {
        try { if (_registered) AppNotificationManager.Default.Unregister(); } catch { }
    }

    /// <summary>Show a notification; failures are logged, never thrown.</summary>
    public static bool Show(AppNotification n, string what)
    {
        try
        {
            AppNotificationManager.Default.Show(n);
            AppLogger.Info($"Notification requested: {what}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Notification could not be shown ({what})", ex);
            return false;
        }
    }

    static string TagOf(DownloadEntry e) => "dl" + (e.AppId.GetHashCode() & 0x7fffffff).ToString("x");

    static void OnListChanged()
    {
        foreach (var e in DownloadRegistry.ActiveEntries())
        {
            lock (Tracked) { if (!Tracked.Add(e.AppId + e.StartedAt.Ticks)) continue; }
            Track(e);
        }
    }

    static void Track(DownloadEntry e)
    {
        var tag = TagOf(e);
        try
        {
            var n = new AppNotificationBuilder()
                .AddText(e.AppName)
                .AddProgressBar(new AppNotificationProgressBar().BindTitle().BindValue().BindValueStringOverride().BindStatus())
                .AddButton(new AppNotificationButton(Loc.T("Cancel")).AddArgument("act", "cancel").AddArgument("app", e.AppId))
                .BuildNotification();
            n.Tag = tag; n.Group = "downloads";
            n.Progress = new AppNotificationProgressData(1) { Title = Loc.T("Downloading"), Value = 0, ValueStringOverride = "", Status = e.FileName };
            Show(n, "download progress " + e.AppName);
        }
        catch (Exception ex) { AppLogger.Warn("Download notification could not be built", ex); return; }

        uint seq = 1; var last = DateTime.MinValue;
        e.Changed += () =>
        {
            if (e.State != DownloadState.Downloading || (DateTime.UtcNow - last).TotalMilliseconds < 1000) return;
            last = DateTime.UtcNow;
            var (r, t) = e.Snapshot;
            var p = new AppNotificationProgressData(++seq)
            {
                Title = Loc.T("Downloading"),
                Value = t is > 0 ? Math.Min(1.0, (double)r / t.Value) : 0,
                ValueStringOverride = DownloadTexts.Status(e),
                Status = e.FileName,
            };
            _ = UpdateAsync(p, tag);
        };
        e.Ended += () => OnEnded(e, tag);
    }

    static async Task UpdateAsync(AppNotificationProgressData p, string tag)
    {
        try
        {
            var res = await AppNotificationManager.Default.UpdateAsync(p, tag, "downloads");
            if (res != AppNotificationProgressResult.Succeeded) AppLogger.Info($"Notification progress update result: {res}");
        }
        catch (Exception ex) { AppLogger.Warn("Notification progress update failed", ex); }
    }

    static void OnEnded(DownloadEntry e, string tag)
    {
        try { _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(tag, "downloads"); } catch { }
        if (e.State == DownloadState.Cancelled) return;
        var b = new AppNotificationBuilder().AddText(e.AppName).AddText(DownloadTexts.Status(e));
        if (e.State == DownloadState.Failed && e.Asset != null)
            b.AddButton(new AppNotificationButton(Loc.T("Retry")).AddArgument("act", "retry").AddArgument("app", e.AppId));
        else if (e.State == DownloadState.InstallUnconfirmed)
            b.AddButton(new AppNotificationButton(Loc.T("Check again")).AddArgument("act", "check").AddArgument("app", e.AppId));
        try { Show(b.BuildNotification(), $"download {e.State} {e.AppName}"); }
        catch (Exception ex) { AppLogger.Warn("Result notification could not be built", ex); }
    }

    static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        args.Arguments.TryGetValue("act", out var act);
        args.Arguments.TryGetValue("app", out var app);
        AppLogger.Info($"Notification action: {act} {app}");
        _ui?.TryEnqueue(() =>
        {
            var entry = app == null ? null : DownloadRegistry.Get(app)
                ?? DownloadRegistry.FinishedEntries().FirstOrDefault(x => x.AppId == app);
            switch (act)
            {
                case "cancel": entry?.Cancel(); break;
                case "retry": if (entry != null) _ = DownloadCoordinator.RetryAsync(entry); break;
                case "check": if (entry != null) DownloadCoordinator.CheckAgain(entry); break;
            }
            App.MainWindowInstance?.Activate();
        });
    }
}
