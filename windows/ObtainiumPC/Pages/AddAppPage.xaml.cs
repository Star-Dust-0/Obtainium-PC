using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Sources;

namespace ObtainiumPC.Pages
{
    public sealed partial class AddAppPage : Page
    {
        public AddAppPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = (string?)null });
            foreach (var name in SourceCatalog.OverrideSourceNames)
                OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = name, Tag = (string?)name });
            OverrideSourceCombo.SelectedIndex = 0;
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (url.Length == 0)
            {
                Show(InfoBarSeverity.Warning, "Enter a source URL first.");
                return;
            }
            AddButton.IsEnabled = false;
            BusyRing.IsActive = true;
            try
            {
                var settings = new Dictionary<string, JsonElement>();
                if (TrackOnlyBox.IsChecked == true)
                    settings["trackOnly"] = JsonSerializer.SerializeToElement(true);
                var overrideSource = (OverrideSourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
                ObtainiumPC.Models.TrackedApp app;
                try
                {
                    app = await App.Updater.CheckAppAsync(url, settings, overrideSource: overrideSource);
                }
                catch (InvalidUrlException) { throw; }
                catch (Exception checkEx) when (App.Settings.AllowAddingAppsWithoutCheck)
                {
                    // lib/pages/add_app.dart: on an unexpected failure, offer to
                    // add a placeholder app (sourceProvider.buildPlaceholderApp).
                    var proceed = new ContentDialog
                    {
                        Title = "Add without checking?",
                        Content = "Fetching the latest version failed (" + checkEx.Message + "). The app can be added anyway and checked later.",
                        PrimaryButtonText = "Add anyway",
                        CloseButtonText = "Cancel",
                        XamlRoot = this.XamlRoot,
                    };
                    if (await proceed.ShowAsync() != ContentDialogResult.Primary) return;
                    string sourceName;
                    try { sourceName = SourceCatalog.Resolve(url, App.Settings, overrideSource).Name; }
                    catch { sourceName = overrideSource ?? "Unknown"; }
                    app = new ObtainiumPC.Models.TrackedApp
                    {
                        // lib/providers/source_provider.dart generateTempID:
                        // bare 12-char lowercase sha256 prefix.
                        Id = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes(url + JsonSerializer.Serialize(settings))))[..12].ToLowerInvariant(),
                        Url = url,
                        Author = sourceName,
                        Name = url,
                        LatestVersion = "unknown",
                        OverrideSource = overrideSource,
                        AdditionalSettings = settings,
                    };
                    app.AssetUrls = new List<KeyValuePair<string, string>> { new("placeholder", "placeholder") };
                }
                // lib/providers/apps_provider.dart addAppsByURL: appAlreadyAdded.
                var dupe = App.TrackedApps.FirstOrDefault(t => t.Id == app.Id || t.Url == app.Url);
                if (dupe != null)
                {
                    Show(InfoBarSeverity.Error, "App already added: " + dupe.FinalName);
                    return;
                }
                App.TrackedApps.Add(app);
                await App.SaveAppsAsync();
                Show(InfoBarSeverity.Success, $"Added {app.FinalName} (latest: {app.LatestVersion ?? "unknown"}).");
                UrlBox.Text = "";
            }
            catch (InvalidUrlException)
            {
                Show(InfoBarSeverity.Error, "That URL is not a supported source. Pick an override source above for other pages (for example HTML).");
            }
            catch (Exception ex)
            {
                Show(InfoBarSeverity.Error, ex.Message);
            }
            finally
            {
                BusyRing.IsActive = false;
                AddButton.IsEnabled = true;
            }
        }

        private sealed class SearchResultItem
        {
            public string Url { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
        }

        // lib/pages/add_app.dart runSearch: query every searchable source,
        // collect per-source errors without failing the whole search.
        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            var query = (SearchBox.Text ?? "").Trim();
            if (query.Length == 0) return;
            SearchButton.IsEnabled = false;
            SearchRing.IsActive = true;
            try
            {
                var items = new List<SearchResultItem>();
                var errors = new List<string>();
                // Searchable sources ported so far (PARITY.md).
                var searchable = new[] { "GitHub", "GitLab", "Forgejo (Codeberg)", "F-Droid", "Uptodown", "RuStore", "Huawei AppGallery" }
                    .Select(n => SourceCatalog.ResolveByName(n, App.Settings))
                    .Where(src => src.CanSearch);
                foreach (var source in searchable)
                {
                    try
                    {
                        var res = await source.SearchAsync(query, new Dictionary<string, JsonElement>());
                        if (res == null) continue;
                        foreach (var (url, v) in res)
                            items.Add(new SearchResultItem { Url = url, Name = v.Count > 0 ? v[0] : url, Description = v.Count > 1 ? v[1] : "" });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(source.Name + ": " + ex.Message);
                    }
                }
                SearchResults.ItemsSource = items;
                SearchResults.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (items.Count == 0 && errors.Count == 0)
                    Show(InfoBarSeverity.Informational, "No results.");
                else if (errors.Count > 0)
                    Show(InfoBarSeverity.Warning, "Some sources failed: " + string.Join("; ", errors));
            }
            finally
            {
                SearchRing.IsActive = false;
                SearchButton.IsEnabled = true;
            }
        }

        private void SearchResults_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SearchResultItem item)
                UrlBox.Text = item.Url;
        }

        private void Show(InfoBarSeverity severity, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }
    }
}
