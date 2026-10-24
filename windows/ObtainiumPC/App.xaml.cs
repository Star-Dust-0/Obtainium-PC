using System.Collections.Generic;
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
        public static AppStore Store { get; private set; } = new AppStore();
        public static AppUpdateService Updater { get; private set; } = new AppUpdateService(Settings);
        public static List<TrackedApp> TrackedApps { get; private set; } = new();
        public static bool Initialized { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            MainWindowInstance = _window;
            _window.Activate();
        }

        public static async Task InitAsync()
        {
            if (Initialized) return;
            Settings = AppSettings.Load();
            Store = new AppStore();
            Updater = new AppUpdateService(Settings);
            Services.Logging.AppLogger.PurgeOld();
            TrackedApps = await Store.LoadAsync();
            Initialized = true;
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
