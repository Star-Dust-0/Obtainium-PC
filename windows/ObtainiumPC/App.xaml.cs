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
            _window.Activate();
        }

        public static async Task InitAsync()
        {
            if (Initialized) return;
            Settings = AppSettings.Load();
            Store = new AppStore();
            Updater = new AppUpdateService(Settings);
            TrackedApps = await Store.LoadAsync();
            Initialized = true;
        }

        public static async Task SaveAppsAsync() => await Store.SaveAsync(TrackedApps);
    }
}
