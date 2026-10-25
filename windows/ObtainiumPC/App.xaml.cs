using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Services.Store;
using ObtainiumPC.Services.Updates;

namespace ObtainiumPC
{
    public partial class App : Application
    {
        private Window? _window;

        public static Window? MainWindowInstance { get; private set; }

        public static AppSettings Settings { get; private set; } = new AppSettings();

        static App()
        {
            // Services.Http.ObtainiumHttp reads the pinning flag through this
            // provider so the pure-logic test project compiles without App.
            Services.Http.ObtainiumHttp.PinningEnabled = () => Settings.EnableCertificatePinning;
        }
        public static AppStore Store { get; private set; } = new AppStore();
        public static AppUpdateService Updater { get; private set; } = new AppUpdateService(Settings);
        public static List<TrackedApp> TrackedApps { get; private set; } = new();
        public static bool Initialized { get; private set; }

        /// An obtainium:// link delivered before the shell finished init.
        public static string? PendingDeepLink { get; set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Single instance (upstream Android is inherently single-instance):
            // a second launch - including an obtainium:// shell open - hands
            // its activation to the running instance and exits, so two
            // processes never race the apps/settings stores.
            var keyInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("ObtainiumPC");
            if (!keyInstance.IsCurrent)
            {
                var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                await keyInstance.RedirectActivationToAsync(activation);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().Activated += (_, a) =>
            {
                if (a.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch
                    && a.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
                    EnqueueDeepLink(launch.Arguments);
            };
            EnqueueDeepLink(args.Arguments);
            _window = new MainWindow();
            MainWindowInstance = _window;
            _window.Activate();
        }

        /// Routes an obtainium:// link from a command line to the shell once
        /// init has finished; stashes it as pending otherwise.
        public static void EnqueueDeepLink(string? commandLine)
        {
            if (!Services.DeepLinkService.TryExtractLink(commandLine, out var link)) return;
            if (MainWindowInstance is MainWindow mw && Initialized)
                mw.DispatcherQueue.TryEnqueue(() => _ = mw.HandleDeepLinkAsync(link));
            else
                PendingDeepLink = link;
        }

        public static async Task InitAsync()
        {
            if (Initialized) return;
            Settings = AppSettings.Load();
            Services.Localization.Loc.Language = Settings.Language;
            Services.Localization.Loc.Changed += () =>
            {
                Settings.Language = Services.Localization.Loc.Language;
                Settings.Save();
                if (MainWindowInstance is MainWindow mw)
                    mw.DispatcherQueue.TryEnqueue(() => mw.ApplyLocalization());
            };
            Store = new AppStore();
            Updater = new AppUpdateService(Settings);
            Services.Logging.AppLogger.PurgeOld();
            TrackedApps = await Store.LoadAsync();
            ReconcileExternalUninstalls();
            Initialized = true;
        }

        // lib/providers/apps_provider_lifecycle.dart reconcileInstallStatus at
        // app load: a tracked app whose OS-confirmed install verifiably
        // disappeared from the registry gets its recorded installedVersion
        // cleared (it renders as not-installed again). Conservative Windows
        // rules live in ExternalUninstallService; a partial/failed probe or
        // any ambiguity preserves the recorded state. The destructive
        // upstream half (removeOnExternalUninstall deleting the tracking
        // record) is intentionally NOT implemented.
        private static void ReconcileExternalUninstalls()
        {
            try
            {
                var probe = Services.Detection.InstalledAppsService.ProbeInstalledApps();
                if (!probe.AllHivesRead) return; // incomplete probe proves nothing
                bool DetectionAllowed(Models.TrackedApp app)
                {
                    if (app.GetSettingBool("trackOnly")) return false;
                    if (!app.GetSettingBool("versionDetection", true)) return false;
                    try
                    {
                        if (Sources.SourceCatalog.TryResolve(app.Url, Settings, out var src)
                            && src is { VersionDetectionDisallowed: true }) return false;
                    }
                    catch { return false; }
                    return true;
                }
                var names = probe.Apps.Select(a => a.DisplayName).ToList();
                // Re-adoption before clearing: a renamed/updated install keeps
                // the app installed under a new DisplayName; only a Strong+
                // match counts (same tiers as detection).
                // Unambiguous identity only: an Exact name match, or a Strong
                // match whose publisher also matches the tracked author. A
                // bare Strong name score is not enough to re-adopt.
                string? FindCurrentMatch(Models.TrackedApp app)
                {
                    var m = Services.Detection.InstalledAppsService.FindMatch(app.FinalName, app.Author, probe.Apps);
                    if (m == null) return null;
                    if (m.Confidence == Services.Detection.InstalledAppsService.MatchConfidence.Exact)
                        return m.Info.DisplayName;
                    if (m.Confidence == Services.Detection.InstalledAppsService.MatchConfidence.Strong
                        && !string.IsNullOrWhiteSpace(app.Author) && m.Info.Publisher != null
                        && Services.Detection.InstalledAppsService.NormalizeName(m.Info.Publisher)
                            .Contains(Services.Detection.InstalledAppsService.NormalizeName(app.Author)))
                        return m.Info.DisplayName;
                    return null;
                }
                var results = Services.Detection.ExternalUninstallService
                    .Reconcile(TrackedApps, names, DetectionAllowed, FindCurrentMatch);
                if (results.Count == 0) return;
                foreach (var r in results)
                {
                    var app = TrackedApps.First(a => a.Id == r.Id);
                    if (r.Action == Services.Detection.ExternalUninstallService.ReconcileAction.Readopt)
                    {
                        Services.Logging.AppLogger.Info(
                            $"Install identity changed for {app.FinalName} ({app.Id}): re-adopted '{r.NewMatchedName}'");
                        app.OsMatchedName = r.NewMatchedName;
                    }
                    else
                    {
                        // SuspectedGone: registry absence is not proof of
                        // uninstall. The recorded install state is preserved;
                        // only the suspicion is logged.
                        Services.Logging.AppLogger.Warn(
                            $"Recorded install for {app.FinalName} ({app.Id}) not found in the registry; recorded state preserved (absence is not proof of uninstall)");
                    }
                }
                _ = SaveAppsAsync();
            }
            catch (Exception ex)
            {
                // A reconcile failure must never touch recorded state.
                Services.Logging.AppLogger.Error("External-uninstall reconcile failed", ex);
            }
        }

        public static async Task SaveAppsAsync()
        {
            await Store.SaveAsync(TrackedApps);
            ScheduleAutoExport();
        }

        // lib/providers/apps_provider.dart scheduleAutoExport: a 2s debounce
        // coalesces bursts of saves into one apps-only export file.
        private static System.Threading.Timer? _autoExportTimer;
        public static void ScheduleAutoExport()
        {
            if (!Settings.AutoExportOnChanges || string.IsNullOrEmpty(Settings.AutoExportPath)) return;
            _autoExportTimer?.Dispose();
            _autoExportTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var json = Services.Store.AppStore.Export(TrackedApps.ToList(), null, 0, "0.1.0");
                    System.IO.File.WriteAllText(Settings.AutoExportPath!, json);
                }
                catch (Exception ex)
                {
                    Services.Logging.AppLogger.Warn("Auto-export failed", ex);
                }
            }, null, 2000, System.Threading.Timeout.Infinite);
        }
    }
}
