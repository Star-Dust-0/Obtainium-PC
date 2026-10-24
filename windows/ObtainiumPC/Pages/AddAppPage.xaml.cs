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
        // lib/pages/add_app.dart openSourcesListDialog: every source as a
        // chip with (track only) / (searchable) markers; clicking opens the
        // source's first host, like upstream's launchUrlString.
        private async void SupportedSources_Click(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 6 };
            var names = new List<string>(SourceCatalog.OverrideSourceNames) { "CoolApk" };
            foreach (var name in names)
            {
                AppSourceBase source;
                try { source = name == "CoolApk" ? new Sources.CoolApk.CoolApkSource() : SourceCatalog.ResolveByName(name, App.Settings); }
                catch { continue; }
                var label = source.Name
                    + (source.SupportsInstall ? "" : " " + Services.Localization.Loc.T("(track only)"))
                    + (source.CanSearch ? " " + Services.Localization.Loc.T("(searchable)") : "");
                var chip = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
                var host = source.Hosts.Count > 0 ? source.Hosts[0] : null;
                if (host != null)
                {
                    var uri = new Uri("https://" + host);
                    chip.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(uri);
                }
                else chip.IsEnabled = false;
                panel.Children.Add(chip);
            }
            var dialog = new ThemedContentDialog
            {
                Title = Services.Localization.Loc.T("Supported sources"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
                CloseButtonText = Services.Localization.Loc.T("OK"),
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }

        public AddAppPage()
        {
            this.InitializeComponent();
            Loaded += (_, _) => Services.Localization.Localizer.Apply(this);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = (string?)null });
            foreach (var name in SourceCatalog.OverrideSourceNames)
                OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = name, Tag = (string?)name });
            OverrideSourceCombo.SelectedIndex = 0;
            // home.dart pushAddApp(initialUrl: data): obtainium://add prefills.
            if (e.Parameter is string prefill && prefill.Length > 0)
                UrlBox.Text = prefill;
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
                    var proceed = new ThemedContentDialog
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
                var searchable = new[] { "GitHub", "GitLab", "Forgejo (Codeberg)", "F-Droid", "F-Droid Third-Party Repo", "Uptodown", "RuStore", "Huawei AppGallery" }
                    .Select(n => SourceCatalog.ResolveByName(n, App.Settings))
                    .Where(src => src.CanSearch);
                foreach (var source in searchable)
                {
                    // lib/pages/add_app.dart runSearch: sources with
                    // includeAdditionalOptsInMainSearch get a per-search
                    // settings dialog before the query.
                    var querySettings = new Dictionary<string, JsonElement>();
                    if (source.IncludeAdditionalOptsInMainSearch)
                    {
                        var collected = await CollectSearchQuerySettings(source);
                        if (collected == null) continue; // cancelled: skip this source
                        querySettings = collected;
                    }
                    try
                    {
                        var res = await source.SearchAsync(query, querySettings);
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

        // lib/pages/add_app.dart runSearch dialog: url field plus the source's
        // searchQuerySettingItemsForUrl (Codeberg: token + minStarCount;
        // F-Droid Third-Party Repo: url only).
        private async Task<Dictionary<string, JsonElement>?> CollectSearchQuerySettings(AppSourceBase source)
        {
            var panel = new StackPanel { Spacing = 10 };
            var isFdroidRepo = source.Name == "F-Droid Third-Party Repo";
            var urlBox = new TextBox
            {
                Header = isFdroidRepo ? "Repository URL" : "Instance URL",
                PlaceholderText = isFdroidRepo ? "https://example.com/fdroid/repo" : "https://codeberg.org",
                Text = isFdroidRepo ? "" : "https://" + (source.Hosts.Count > 0 ? source.Hosts[0] : "codeberg.org"),
            };
            panel.Children.Add(urlBox);
            PasswordBox? tokenBox = null;
            TextBox? minStarsBox = null;
            if (source.Name == "Forgejo (Codeberg)")
            {
                tokenBox = new PasswordBox { Header = "Personal access token (optional)" };
                panel.Children.Add(tokenBox);
                minStarsBox = new TextBox { Header = "Minimum star count", Text = "0" };
                panel.Children.Add(minStarsBox);
            }
            var dialog = new ThemedContentDialog
            {
                Title = "Search " + source.Name,
                Content = panel,
                PrimaryButtonText = "Search",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            var settings = new Dictionary<string, JsonElement>();
            if (urlBox.Text.Trim().Length > 0)
                settings["url"] = JsonSerializer.SerializeToElement(urlBox.Text.Trim());
            if (tokenBox != null && tokenBox.Password.Length > 0)
                settings["forgejo-creds"] = JsonSerializer.SerializeToElement(tokenBox.Password);
            if (minStarsBox != null)
                settings["minStarCount"] = JsonSerializer.SerializeToElement(minStarsBox.Text.Trim());
            return settings;
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
