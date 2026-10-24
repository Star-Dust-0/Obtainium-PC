using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ObtainiumPC.Pages
{
    public sealed partial class ImportExportPage : Page
    {
        public ImportExportPage()
        {
            this.InitializeComponent();
        }

        private static void InitPicker(object picker)
        {
            if (App.MainWindowInstance == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        private void ShowResult(InfoBarSeverity severity, string title, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Title = title;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            await App.InitAsync();
            var picker = new FileSavePicker
            {
                SuggestedFileName = "obtainium-export",
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitPicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var level = ExportLevelCombo.SelectedIndex; // 0 apps, 1 +settings, 2 +creds
            Dictionary<string, object?>? settings = null;
            if (level > 0)
            {
                var el = JsonSerializer.SerializeToElement(App.Settings);
                settings = el.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
                if (level < 2) settings.Remove(nameof(Services.Settings.AppSettings.GitHubPersonalAccessToken));
            }
            var json = Services.Store.AppStore.Export(App.TrackedApps, settings, level, "0.1.0");
            await FileIO.WriteTextAsync(file, json);
            ShowResult(InfoBarSeverity.Success, "Export complete",
                $"{App.TrackedApps.Count} app(s) written to {file.Name}.");
        }

        private async void ImportStars_Click(object sender, RoutedEventArgs e)
        {
            await App.InitAsync();
            var username = StarsUsernameBox.Text.Trim();
            if (username.Length == 0)
            {
                ShowResult(InfoBarSeverity.Warning, "Username required", "Enter the GitHub username whose starred repos you want to import.");
                return;
            }

            List<Services.BulkImport.GitHubStarsService.StarredRepo> repos;
            try
            {
                repos = await Services.BulkImport.GitHubStarsService.GetStarredReposAsync(username, App.Settings);
            }
            catch (Exception ex)
            {
                ShowResult(InfoBarSeverity.Error, "Could not fetch starred repos", ex.Message);
                return;
            }
            if (repos.Count == 0)
            {
                ShowResult(InfoBarSeverity.Informational, "Nothing found", $"No starred repos for '{username}'.");
                return;
            }

            // Review before adding: every repo starts checked, duplicates unchecked.
            var existingUrls = App.TrackedApps.Select(a => a.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var items = repos.Select(r =>
            {
                var isDuplicate = existingUrls.Contains(r.Url);
                return new CheckBox
                {
                    Content = string.IsNullOrEmpty(r.Description) ? r.FullName : $"{r.FullName} - {r.Description}",
                    IsChecked = !isDuplicate,
                    IsEnabled = !isDuplicate,
                    Tag = r,
                };
            }).ToList();
            var listView = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.None, MaxHeight = 420 };
            var dialog = new ContentDialog
            {
                Title = $"Starred repos of {username} ({repos.Count})",
                Content = new ScrollViewer { Content = listView },
                PrimaryButtonText = "Add selected",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var added = 0;
            foreach (var checkbox in items)
            {
                if (checkbox.IsChecked != true || checkbox.Tag is not Services.BulkImport.GitHubStarsService.StarredRepo repo) continue;
                // The URL is standardized and resolved on the first update check.
                var app = new Models.TrackedApp
                {
                    Id = Services.Updates.AppUpdateService.GenerateTempId(repo.Url),
                    Url = repo.Url,
                    Name = repo.FullName.Split('/').Last(),
                    Author = repo.FullName.Split('/').First(),
                };
                App.TrackedApps.Add(app);
                added++;
            }
            await App.SaveAppsAsync();
            ShowResult(InfoBarSeverity.Success, "Starred repos added",
                $"Added {added} app(s). They resolve their first version on the next update check.");
        }

        // lib/pages/import_export.dart: URL-list import -> addAppsByURL with a
        // per-URL error report.
        private async void ImportUrls_Click(object sender, RoutedEventArgs e)
        {
            var urls = (UrlListBox.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim()).Where(u => u.Length > 0).Distinct().ToList();
            if (urls.Count == 0) return;
            UrlImportButton.IsEnabled = false;
            UrlImportRing.IsActive = true;
            var errors = new List<string>();
            var added = 0;
            try
            {
                foreach (var url in urls)
                {
                    try
                    {
                        var app = await App.Updater.CheckAppAsync(url, new Dictionary<string, JsonElement>());
                        var dupe = App.TrackedApps.FirstOrDefault(t => t.Id == app.Id || t.Url == app.Url);
                        if (dupe != null)
                        {
                            errors.Add(url + ": already added (" + dupe.FinalName + ")");
                            continue;
                        }
                        App.TrackedApps.Add(app);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(url + ": " + ex.Message);
                    }
                }
                if (added > 0) await App.SaveAppsAsync();
                if (errors.Count == 0)
                {
                    ShowResult(InfoBarSeverity.Success, "Imported", $"Imported {added} app(s).");
                    UrlListBox.Text = "";
                }
                else
                {
                    var dlg = new ContentDialog
                    {
                        Title = $"Imported {added} of {urls.Count}",
                        Content = new ScrollViewer
                        {
                            MaxHeight = 360,
                            Content = new TextBlock { Text = string.Join("\n", errors), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                        },
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                    };
                    await dlg.ShowAsync();
                }
            }
            finally
            {
                UrlImportRing.IsActive = false;
                UrlImportButton.IsEnabled = true;
            }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            await App.InitAsync();
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitPicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var (apps, hasSettings, _) = Services.Store.AppStore.Import(json);
                var added = 0;
                var replaced = 0;
                foreach (var app in apps)
                {
                    var i = App.TrackedApps.FindIndex(x => x.Id == app.Id);
                    if (i >= 0) { App.TrackedApps[i] = app; replaced++; }
                    else { App.TrackedApps.Add(app); added++; }
                }
                await App.SaveAppsAsync();
                var settingsNote = hasSettings
                    ? " The file also contains settings; applying them lands with the settings import step."
                    : "";
                ShowResult(InfoBarSeverity.Success, "Import complete",
                    $"{added} app(s) added, {replaced} replaced.{settingsNote}");
            }
            catch (Services.Store.ObtainiumImportException ex)
            {
                ShowResult(InfoBarSeverity.Error, "Import failed", ex.Message);
            }
            catch (Exception ex)
            {
                ShowResult(InfoBarSeverity.Error, "Import failed", ex.Message);
            }
        }
    }
}
