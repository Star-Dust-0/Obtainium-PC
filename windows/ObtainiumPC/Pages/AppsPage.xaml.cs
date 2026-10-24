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
        public AppRow(TrackedApp app, bool hasUpdate, bool healthFlagged)
        {
            App = app;
            HasUpdate = hasUpdate;
            HealthFlagged = healthFlagged;
        }

        public TrackedApp App { get; }
        public bool HasUpdate { get; }
        public bool HealthFlagged { get; }

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
                if (HealthFlagged) return "Update-health issue";
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
                        ReloadCategoryCombo();
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
            var query = SearchBox?.Text?.Trim() ?? "";
            var updatesOnly = UpdatesOnlyToggle?.IsChecked == true;
            var showHealth = App.Settings.ShowUpdateHealthIndicator;

            Rows.Clear();
            // lib/components/app_list_tile.dart AppListBuilder.sort via Settings
            // (sortColumn/sortOrder).
            var sorted = Services.Store.AppSort.Sort(App.TrackedApps,
                App.Settings.SortColumn, App.Settings.SortOrder);
            // lib/components/app_list_tile.dart AppListBuilder.reorder
            sorted = Services.Store.AppSort.Reorder(sorted,
                App.Settings.PinUpdates, App.Settings.BuryNonInstalled,
                App.Updater.IsAppUpdateable);
            foreach (var app in sorted)
            {
                var hasUpdate = App.Updater.IsAppUpdateable(app);
                if (updatesOnly && !hasUpdate) continue;
                if (CategoryCombo.SelectedIndex > 0
                    && CategoryCombo.SelectedItem is string cat
                    && !app.Categories.Contains(cat)) continue;
                if (query.Length > 0
                    && !app.FinalName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !app.FinalAuthor.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !app.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                var healthFlagged = showHealth && Services.Updates.UpdateHealthService.HasIssues(app, App.Settings);
                Rows.Add(new AppRow(app, hasUpdate, healthFlagged));
            }
            EmptyState.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ReloadRows();
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => ReloadRows();

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ReloadRows();

        private void ReloadCategoryCombo()
        {
            var keep = CategoryCombo.SelectedItem as string;
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add("All");
            foreach (var c in App.TrackedApps.SelectMany(a => a.Categories).Distinct().OrderBy(c => c))
                CategoryCombo.Items.Add(c);
            CategoryCombo.SelectedItem = keep != null && CategoryCombo.Items.Contains(keep) ? keep : "All";
            if (CategoryCombo.SelectedItem == null) CategoryCombo.SelectedIndex = 0;
        }

        // lib/pages/apps.dart categorize batch action (SelectionModal ->
        // checkboxes here; upstream color management not ported).
        private async void BatchCategorize_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var all = App.TrackedApps.SelectMany(a => a.Categories).Distinct().OrderBy(c => c).ToList();
            var preChecked = sel.SelectMany(a => a.Categories).ToHashSet();
            var panel = new StackPanel { Spacing = 6 };
            var boxes = new List<CheckBox>();
            foreach (var c in all)
            {
                var box = new CheckBox { Content = c, IsChecked = preChecked.Contains(c) };
                boxes.Add(box);
                panel.Children.Add(box);
            }
            var newBox = new TextBox { PlaceholderText = "New category", Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(newBox);
            var dlg = new ContentDialog
            {
                Title = sel.Count == 1 ? "Categorize 1 app" : $"Categorize {sel.Count} apps",
                Content = new ScrollViewer { MaxHeight = 360, Content = panel },
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var chosen = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
            var newCat = (newBox.Text ?? "").Trim();
            if (newCat.Length > 0 && !chosen.Contains(newCat)) chosen.Add(newCat);
            foreach (var a in sel) a.Categories = chosen.ToList();
            await App.SaveAppsAsync();
            ReloadCategoryCombo();
            ReloadRows();
        }

        // lib/pages/apps.dart selection mode + bottom-sheet batch actions.
        private void SelectToggle_Changed(object sender, RoutedEventArgs e)
        {
            var selecting = SelectToggle.IsChecked == true;
            AppsList.SelectionMode = selecting ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            AppsList.IsItemClickEnabled = !selecting;
            BatchBar.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
            if (!selecting) AppsList.SelectedItems.Clear();
        }

        private List<TrackedApp> SelectedApps() =>
            AppsList.SelectedItems.OfType<AppRow>().Select(r => r.App).ToList();

        private void ExitSelection()
        {
            SelectToggle.IsChecked = false;
            SelectToggle_Changed(this, new RoutedEventArgs());
        }

        // lib/pages/apps.dart pinSelectedApps: unpin only when every selected
        // app is already pinned.
        private async void BatchPin_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var target = !sel.All(a => a.Pinned);
            foreach (var a in sel) a.Pinned = target;
            await App.SaveAppsAsync();
            ReloadRows();
        }

        private async void BatchDisable_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var target = !sel.All(a => a.Disabled);
            foreach (var a in sel) a.Disabled = target;
            await App.SaveAppsAsync();
            ReloadRows();
        }

        // lib/pages/apps.dart showMassMarkDialog (simplified: mark as updated
        // only; the extra "mark even without update" option is not ported).
        private async void BatchMark_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps().Where(a => a.InstalledVersion != a.LatestVersion).ToList();
            if (sel.Count == 0) return;
            foreach (var a in sel) a.InstalledVersion = a.LatestVersion;
            await App.SaveAppsAsync();
            ReloadRows();
            ExitSelection();
        }

        // lib/pages/apps.dart shareAppURLs (copied to the clipboard; Windows
        // share-sheet integration is a disclosed simplification).
        private void BatchCopyUrls_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(string.Join("\n", sel.Select(a => a.Url)));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }

        private async void BatchRemove_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var dialog = new ContentDialog
            {
                Title = sel.Count == 1 ? "Remove 1 app?" : $"Remove {sel.Count} apps?",
                Content = "The apps themselves are not uninstalled; Obtainium-PC just stops tracking them.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            foreach (var a in sel) App.TrackedApps.Remove(a);
            await App.SaveAppsAsync();
            ReloadRows();
            ExitSelection();
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
