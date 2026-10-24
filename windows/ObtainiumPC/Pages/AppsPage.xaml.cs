using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Models;

namespace ObtainiumPC.Pages
{
    public sealed class AppRow
    {
        public AppRow(TrackedApp app, bool hasUpdate)
        {
            App = app;
            HasUpdate = hasUpdate;
        }

        public TrackedApp App { get; }
        public bool HasUpdate { get; }

        public string Title => App.FinalName;

        public string Subtitle
        {
            get
            {
                var installed = App.InstalledVersion ?? "not installed";
                var latest = App.LatestVersion ?? "unknown";
                return $"{App.FinalAuthor}  -  {installed} -> {latest}";
            }
        }

        public string Status
        {
            get
            {
                if (App.Disabled) return "Tracking disabled";
                if (HasUpdate) return "Update available";
                if (App.ConsecutiveFailedChecks > 0) return $"Last check failed ({App.ConsecutiveFailedChecks}x)";
                return "";
            }
        }

        public Visibility StatusVisibility =>
            string.IsNullOrEmpty(Status) ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed partial class AppsPage : Page
    {
        public ObservableCollection<AppRow> Rows { get; } = new();

        public AppsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            Services.Updates.BackgroundUpdateService.Instance.AppsUpdated += OnAppsUpdated;
            ReloadRows();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Services.Updates.BackgroundUpdateService.Instance.AppsUpdated -= OnAppsUpdated;
            base.OnNavigatedFrom(e);
        }

        private void OnAppsUpdated(object? sender, EventArgs e) => ReloadRows();

        private void ReloadRows()
        {
            Rows.Clear();
            foreach (var app in App.TrackedApps)
                Rows.Add(new AppRow(app, App.Updater.IsAppUpdateable(app)));
            EmptyState.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AppsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AppRow row)
                Frame.Navigate(typeof(AppDetailPage), row.App.Id);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            BusyRing.IsActive = true;
            try
            {
                // Manual refresh checks every non-disabled app, including ones
                // exempt from background updates.
                await Services.Updates.BackgroundUpdateService.Instance.CheckAllAsync(manual: true);
            }
            finally
            {
                BusyRing.IsActive = false;
                RefreshButton.IsEnabled = true;
                ReloadRows();
            }
        }
    }
}
