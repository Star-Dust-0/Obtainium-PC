using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Models;
using ObtainiumPC.Sources;
using ObtainiumPC.Services.Download;

namespace ObtainiumPC.Pages
{
    /// lib/pages/apps.dart _getGroupCollapsibleTile: header row for a
    /// category/source group; click toggles collapse (per-session state).
    public sealed class GroupHeaderRow
    {
        public GroupHeaderRow(string? key, string title, int count, bool expanded)
        {
            Key = key; Title = title; Count = count; Expanded = expanded;
        }

        public string? Key { get; }
        public string Title { get; }
        public int Count { get; }
        public bool Expanded { get; }
        public string CountText => Count == 1
            ? string.Format(Services.Localization.Loc.T("{0} app"), Count)
            : string.Format(Services.Localization.Loc.T("{0} apps"), Count);
        public string ChevronGlyph => Expanded ? "\uE70D" : "\uE76C";
    }

    public sealed class AppsRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? AppTemplate { get; set; }
        public DataTemplate? GroupTemplate { get; set; }
        protected override DataTemplate? SelectTemplateCore(object item) =>
            item is GroupHeaderRow ? GroupTemplate : AppTemplate;
    }

    public sealed class AppRow
    {
        public AppRow(TrackedApp app, bool hasUpdate, bool healthFlagged)
        {
            App = app;
            HasUpdate = hasUpdate;
            HealthFlagged = healthFlagged;
        }

        // lib/components/app_list_tile.dart AppTileMetrics: compact trims
        // vertical padding + icon, dense removes padding.
        public void ApplyDensity(string density)
        {
            RowPadding = density == "dense" ? new Thickness(12, 2, 12, 2)
                : density == "compact" ? new Thickness(12, 6, 12, 6)
                : new Thickness(12, 12, 12, 12);
            IconSize = density == "compact" ? 18 : 22;
        }

        public Thickness RowPadding { get; private set; } = new Thickness(12);
        public double IconSize { get; private set; } = 22;

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

        public string CategoriesText =>
            App.Categories.Count > 0 ? string.Join("  -  ", App.Categories) : "";

        public Visibility CategoriesVisibility =>
            App.Categories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string Status
        {
            get
            {
                if (App.Disabled) return "Tracking disabled";
                if (HealthFlagged) return "Update-health issue";
                if (HasUpdate) return Services.Localization.Loc.T("Update available");
                if (App.ConsecutiveFailedChecks > 0) return $"Last check failed ({App.ConsecutiveFailedChecks}x)";
                return "";
            }
        }

        public Visibility StatusVisibility =>
            string.IsNullOrEmpty(Status) ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed partial class AppsPage : Page
    {
        public ObservableCollection<object> Rows { get; } = new();
        private readonly HashSet<string?> _collapsedGroups = new();
        private bool _collapseInitDone;

        public AppsPage()
        {
            this.InitializeComponent();
            Loaded += (_, _) => Services.Localization.Localizer.Apply(this);
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
            var query = (SearchBoxInline.Visibility == Visibility.Visible ? SearchBoxInline.Text : SearchBox.Text)?.Trim() ?? "";
            var updatesOnly = UpdatesOnlyToggle?.IsChecked == true;
            var showHealth = App.Settings.ShowUpdateHealthIndicator;

            Rows.Clear();
            var filtered = new List<AppRow>();
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
                var row = new AppRow(app, hasUpdate, healthFlagged);
                row.ApplyDensity(App.Settings.ListDensity);
                filtered.Add(row);
            }

            // lib/pages/apps.dart _buildGroups + _getGroupCollapsibleTile:
            // category/source grouping with collapsible headers. An app with
            // several categories appears in each of its groups; the
            // uncategorized group sorts last; collapse state is per-session.
            var groupBy = App.Settings.GroupBy;
            if (groupBy == "none")
            {
                foreach (var row in filtered) Rows.Add(row);
            }
            else
            {
                var groups = new Dictionary<string?, List<AppRow>>();
                foreach (var row in filtered)
                {
                    if (groupBy == "category")
                    {
                        if (row.App.Categories.Count == 0)
                        {
                            if (!groups.TryGetValue(null, out var l)) groups[null] = l = new List<AppRow>();
                            l.Add(row);
                        }
                        else
                        {
                            foreach (var cat in row.App.Categories)
                            {
                                if (!groups.TryGetValue(cat, out var l)) groups[cat] = l = new List<AppRow>();
                                l.Add(row);
                            }
                        }
                    }
                    else
                    {
                        string? label = null;
                        if (SourceCatalog.TryResolve(row.App.Url, App.Settings, out var src, row.App.OverrideSource) && src != null)
                            label = src.Name;
                        if (!groups.TryGetValue(label, out var l)) groups[label] = l = new List<AppRow>();
                        l.Add(row);
                    }
                }
                var keys = groups.Keys.ToList();
                keys.Sort((a, b) => a == null && b == null ? 0 : a == null ? 1 : b == null ? -1
                    : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
                if (!_collapseInitDone)
                {
                    _collapseInitDone = true;
                    if (App.Settings.CollapseGroupsOnStartup)
                        foreach (var k in keys) _collapsedGroups.Add(k);
                }
                foreach (var key in keys)
                {
                    var expanded = !_collapsedGroups.Contains(key);
                    var title = key ?? Services.Localization.Loc.T(groupBy == "category" ? "No category" : "No source");
                    if (groupBy == "category" && key != null && key.Length > 0)
                        title = char.ToUpperInvariant(key[0]) + key.Substring(1);
                    Rows.Add(new GroupHeaderRow(key, title, groups[key].Count, expanded));
                    if (expanded)
                        foreach (var row in groups[key]) Rows.Add(row);
                }
            }
            EmptyState.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e)
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            // Keep the inline (wide) and full-row (narrow) boxes in sync.
            if (!ReferenceEquals(sender, SearchBox) && SearchBox.Text != SearchBoxInline.Text)
                SearchBox.Text = SearchBoxInline.Text;
            else if (!ReferenceEquals(sender, SearchBoxInline) && SearchBoxInline.Text != SearchBox.Text)
                SearchBoxInline.Text = SearchBox.Text;
            ReloadRows();
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => ReloadRows();

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ReloadRows();

        private void ReloadCategoryCombo()
        {
            var keep = CategoryCombo.SelectedItem as string;
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(Services.Localization.Loc.T("All"));
            foreach (var c in App.TrackedApps.SelectMany(a => a.Categories).Distinct().OrderBy(c => c))
                CategoryCombo.Items.Add(c);
            CategoryCombo.SelectedItem = keep != null && CategoryCombo.Items.Contains(keep) ? keep : Services.Localization.Loc.T("All");
            if (CategoryCombo.SelectedItem == null) CategoryCombo.SelectedIndex = 0;
        }

        // lib/providers/apps_provider_install.dart downloadAppAssets: each app
        // gets the asset picker (confirmAppFileUrl evenIfSingleChoice), then a
        // sequential download; errors are collected per app.
        private async void BatchDownload_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps().Where(a => App.Updater.IsAppUpdateable(a)).ToList();
            if (sel.Count == 0) return;
            var results = new List<string>();
            foreach (var app in sel)
            {
                var assets = app.AssetUrls.Concat(app.OtherAssetUrls).ToList();
                if (assets.Count == 0)
                {
                    results.Add(app.FinalName + ": no downloadable asset on the latest release");
                    continue;
                }
                var index = app.PreferredAssetIndex >= 0 && app.PreferredAssetIndex < assets.Count ? app.PreferredAssetIndex : 0;
                var filterRe = app.AdditionalSettings.TryGetValue("apkFilterRegEx", out var fr)
                    && fr.ValueKind == System.Text.Json.JsonValueKind.String ? fr.GetString() : null;
                if (!string.IsNullOrEmpty(filterRe))
                {
                    try
                    {
                        var rx = new System.Text.RegularExpressions.Regex(filterRe);
                        var matchIdx = assets.FindIndex(a => rx.IsMatch(a.Key));
                        if (matchIdx >= 0) index = matchIdx;
                    }
                    catch { }
                }
                var picked = await PickAssetDialogAsync(app.FinalName, assets, index);
                if (picked < 0)
                {
                    results.Add(app.FinalName + ": skipped");
                    continue;
                }
                var asset = assets[picked];
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Width = 320 };
                var progDlg = new ContentDialog { Title = "Downloading " + app.FinalName, Content = bar, XamlRoot = this.XamlRoot };
                var showOp = progDlg.ShowAsync();
                try
                {
                    var progress = new Progress<(long Received, long? Total)>(pr =>
                    {
                        if (pr.Total is > 0) bar.Value = Math.Min(100, (double)pr.Received / pr.Total.Value * 100);
                    });
                    var url = asset.Value;
                    SourceCatalog.TryResolve(app.Url, App.Settings, out var dlSource);
                    if (dlSource != null)
                        url = await dlSource.AssetUrlPrefetchModifierAsync(url, app.Url, app.AdditionalSettings);
                    var pat = url.Contains("github.com") || url.Contains("githubusercontent.com")
                        ? App.Settings.GitHubPersonalAccessToken : null;
                    var headers = dlSource?.GetDownloadRequestHeaders(url, app.Url, app.AdditionalSettings);
                    var path = await new DownloadService().DownloadAsync(url, asset.Key, progress, pat, headers);
                    app.AssetSizeBytes = new System.IO.FileInfo(path).Length;
                    results.Add(app.FinalName + ": downloaded " + asset.Key);
                    if (dlSource?.SupportsInstall != false) DownloadService.OpenInstaller(path);
                }
                catch (Exception ex)
                {
                    results.Add(app.FinalName + ": failed - " + ex.Message);
                }
                finally
                {
                    progDlg.Hide();
                    try { await showOp; } catch { }
                }
            }
            await App.SaveAppsAsync();
            ReloadRows();
            ExitSelection();
            var summary = new ContentDialog
            {
                Title = "Downloads",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock { Text = string.Join("\n", results), TextWrapping = TextWrapping.Wrap },
                },
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
            };
            await summary.ShowAsync();
        }

        private async Task<int> PickAssetDialogAsync(string appName, List<KeyValuePair<string, string>> assets, int preselect)
        {
            var selected = -1;
            var panel = new StackPanel { Spacing = 4 };
            var radios = new List<RadioButton>();
            for (var i = 0; i < assets.Count; i++)
            {
                var radio = new RadioButton { Content = assets[i].Key, GroupName = "batchPick" };
                var captured = i;
                radio.Checked += (s2, e2) => selected = captured;
                radios.Add(radio);
                panel.Children.Add(radio);
            }
            radios[Math.Clamp(preselect, 0, radios.Count - 1)].IsChecked = true;
            var dialog = new ContentDialog
            {
                Title = "Choose a file - " + appName,
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = "Download",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? selected : -1;
        }

        // lib/pages/apps.dart shareExport: an Obtainium export of the selected
        // apps only (apps-only level; written to a file the user picks).
        private async void BatchExport_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = "obtainium-export-selected",
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var json = Services.Store.AppStore.Export(sel, null, 0, "0.1.0");
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
        }

        // lib/pages/apps.dart shareConfigLinks: obtainium:// deep links wrapped
        // in the redirect URL, shared through the Windows share sheet.
        private void BatchCopyConfigs_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var lines = sel.Select(a =>
            {
                var inner = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["id"] = a.Id,
                    ["url"] = a.Url,
                    ["author"] = a.Author,
                    ["name"] = a.Name,
                    ["preferredApkIndex"] = a.PreferredAssetIndex,
                    ["additionalSettings"] = a.AdditionalSettingsJson,
                    ["overrideSource"] = a.OverrideSource,
                });
                return "https://apps.obtainium.imranr.dev/redirect?r=obtainium://app/" + Uri.EscapeDataString(inner);
            });
            Services.ShareService.ShareText("Obtainium - Apps", string.Join("\n", lines));
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
            // lib/components/category_editor.dart: new categories join the
            // registry with a random light color.
            foreach (var c in chosen)
            {
                if (!App.Settings.CategoryColors.ContainsKey(c))
                    App.Settings.CategoryColors[c] = (int)(0xFF000000
                        | (uint)(150 + Random.Shared.Next(105)) << 16
                        | (uint)(150 + Random.Shared.Next(105)) << 8
                        | (uint)(150 + Random.Shared.Next(105)));
            }
            App.Settings.Save();
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

        // lib/pages/apps.dart showMassMarkDialog: confirm first, and only mark
        // apps that are installed AND lack standard version detection
        // (isVersionDetectionPossible == false).
        private async void BatchMark_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            var confirm = new ContentDialog
            {
                Title = sel.Count == 1 ? "Mark 1 selected app as updated?" : $"Mark {sel.Count} selected apps as updated?",
                Content = "This only works for apps without standard version detection (track-only, release-date-as-version, or sources that disallow version detection).",
                PrimaryButtonText = "Mark updated",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            var marked = 0;
            foreach (var a in sel)
            {
                if (a.InstalledVersion == null) continue;
                var trackOnly = a.AdditionalSettings.TryGetValue("trackOnly", out var to)
                    && to.ValueKind == System.Text.Json.JsonValueKind.True;
                var rdAsVersion = a.AdditionalSettings.TryGetValue("releaseDateAsVersion", out var rd)
                    && rd.ValueKind == System.Text.Json.JsonValueKind.True;
                SourceCatalog.TryResolve(a.Url, App.Settings, out var src);
                var disallowed = src?.VersionDetectionDisallowed == true;
                var htmlNoDetection = src?.Name == "HTML"
                    && !(a.AdditionalSettings.TryGetValue("versionExtractionRegEx", out var ve)
                        && ve.ValueKind == System.Text.Json.JsonValueKind.String
                        && !string.IsNullOrEmpty(ve.GetString()));
                if (trackOnly || rdAsVersion || disallowed || htmlNoDetection)
                {
                    a.InstalledVersion = a.LatestVersion;
                    marked++;
                }
            }
            await App.SaveAppsAsync();
            ReloadRows();
            ExitSelection();
        }

        // lib/pages/apps.dart shareAppURLs through the Windows share sheet.
        private void BatchCopyUrls_Click(object sender, RoutedEventArgs e)
        {
            var sel = SelectedApps();
            if (sel.Count == 0) return;
            Services.ShareService.ShareText("Obtainium - Apps", string.Join("\n", sel.Select(a => a.Url)));
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
            else if (e.ClickedItem is GroupHeaderRow header)
            {
                // lib/pages/apps.dart toggleGroupCollapse.
                if (!_collapsedGroups.Remove(header.Key)) _collapsedGroups.Add(header.Key);
                ReloadRows();
            }
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
