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
            // Persisted like upstream settings_provider exportSettings; the
            // same level drives auto-export on changes.
            ExportLevelCombo.SelectedIndex = App.Settings.ExportSettingsLevel;
            ExportSecretsWarning.IsOpen = App.Settings.ExportSettingsLevel >= 2;
            ExportLevelCombo.SelectionChanged += (_, _) =>
            {
                if (ExportLevelCombo.SelectedIndex >= 0)
                {
                    App.Settings.ExportSettingsLevel = ExportLevelCombo.SelectedIndex;
                    App.Settings.Save();
                    // Level 2 stays tied to this explicit selection; the warning
                    // (upstream exportIncludesSecretsWarning) stays visible while
                    // it is selected, and it also covers auto-export-on-changes.
                    ExportSecretsWarning.IsOpen = ExportLevelCombo.SelectedIndex >= 2;
                }
            };
            Loaded += (_, _) => Services.Localization.Localizer.Apply(this);
            Loaded += (s2, e2) =>
            {
                InstalledOnlyToggle.IsOn = App.Settings.ExportInstalledOnly;
                AutoExportToggle.IsOn = App.Settings.AutoExportOnChanges;
                AutoExportPathBox.Text = App.Settings.AutoExportPath ?? "";
            };
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
            // lib/pages/import_export.dart runObtainiumExport: a secrets-level
            // export re-confirms every time.
            if (level >= 2)
            {
                var confirm = new ContentDialog
                {
                    Title = Services.Localization.Loc.T("Warning"),
                    Content = Services.Localization.Loc.T("The exported file will contain all settings in cleartext, which may include sensitive values such as tokens. Anyone with the file can read them."),
                    PrimaryButtonText = Services.Localization.Loc.T("Continue"),
                    CloseButtonText = Services.Localization.Loc.T("Cancel"),
                    XamlRoot = this.XamlRoot,
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }
            Dictionary<string, object?>? settings = null;
            if (level > 0)
            {
                var el = JsonSerializer.SerializeToElement(App.Settings);
                settings = el.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
                if (level < 2) settings.Remove(nameof(Services.Settings.AppSettings.GitHubPersonalAccessToken));
            }
            // lib/providers/settings_provider.dart exportInstalledOnly
            var toExport = App.Settings.ExportInstalledOnly
                ? App.SnapshotApps().Where(a => a.InstalledVersion != null).ToList()
                : App.SnapshotApps();
            var json = Services.Store.AppStore.Export(toExport, settings, level, "0.1.0");
            // The auto-export target and this manual target can be the same file. Then both
            // writers go through the same serialized, atomic path so neither can truncate or
            // interleave with the other; otherwise keep the picker-granted write.
            if (Services.Store.AppStore.SamePath(file.Path, App.Settings.AutoExportPath))
                Services.Store.AppStore.WriteFileAtomic(file.Path, json);
            else
                await FileIO.WriteTextAsync(file, json);
            ShowResult(InfoBarSeverity.Success, "Export complete",
                string.Format(Services.Localization.Loc.T("{0} apps written to {1}."), App.TrackedApps.Count, file.Name));
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
                Services.Logging.AppLogger.Error("Fetching starred repos failed", ex);
                ShowResult(InfoBarSeverity.Error, "Could not fetch starred repos",
                    Services.Localization.Loc.T("The request failed. Details are in the logs."));
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
            var dialog = new ThemedContentDialog
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
                if (App.TryAddApp(app) == null) added++;
            }
            await App.SaveAppsAsync();
            ShowResult(InfoBarSeverity.Success, "Starred repos added",
                string.Format(Services.Localization.Loc.T("Added {0} apps. They resolve their first version on the next update check."), added));
        }

        // lib/pages/import_export.dart: URL-list import -> addAppsByURL with a
        // per-URL error report.
        private void InstalledOnlyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            App.Settings.ExportInstalledOnly = InstalledOnlyToggle.IsOn;
            App.Settings.Save();
        }

        // lib/pages/import_export.dart importFromFile: pull http(s) URLs out of
        // a picked file and keep only ones a source recognizes.
        private async void ImportUrlFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add("*");
            InitPicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var contents = await FileIO.ReadTextAsync(file);
            var urls = System.Text.RegularExpressions.Regex.Matches(contents, @"https?://[^\s""]+")
                .Select(m => m.Value)
                .Distinct()
                .Where(u => Sources.SourceCatalog.TryResolve(u, App.Settings, out _))
                .ToList();
            if (urls.Count == 0)
            {
                ShowResult(InfoBarSeverity.Warning, "No URLs", "No supported app URLs found in that file.");
                return;
            }
            UrlListBox.Text = string.Join("\n", urls);
            ShowResult(InfoBarSeverity.Informational, "URLs loaded", $"{urls.Count} URL(s) loaded - review and tap Import URLs.");
        }

        private void AutoExportToggle_Toggled(object sender, RoutedEventArgs e)
        {
            App.Settings.AutoExportOnChanges = AutoExportToggle.IsOn;
            App.Settings.Save();
        }

        // Pick the auto-export target with the file picker (upstream picks a
        // directory; the Windows port stores the full file path).
        private async void AutoExportBrowse_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = "obtainium-export",
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitPicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            AutoExportPathBox.Text = file.Path;
            App.Settings.AutoExportPath = file.Path;
            App.Settings.Save();
        }

        private void AutoExportPathBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var path = (AutoExportPathBox.Text ?? "").Trim();
            App.Settings.AutoExportPath = path.Length > 0 ? path : null;
            App.Settings.Save();
        }

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
                        var dupe = App.TryAddApp(app);
                        if (dupe != null)
                        {
                            errors.Add(url + ": already added (" + dupe.FinalName + ")");
                            continue;
                        }
                        added++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(url + ": " + Services.Localization.SourceErrorText.Describe(ex, Services.Localization.Loc.T));
                    }
                }
                if (added > 0) await App.SaveAppsAsync();
                if (errors.Count == 0)
                {
                    ShowResult(InfoBarSeverity.Success, "Imported", string.Format(Services.Localization.Loc.T("Imported {0} apps."), added));
                    UrlListBox.Text = "";
                }
                else
                {
                    var dlg = new ThemedContentDialog
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
            await ImportJsonAsync(await FileIO.ReadTextAsync(file));
        }

        private async Task ImportJsonAsync(string json)
        {
            await App.InitAsync();
            try
            {
                var (allApps, hasSettings, _) = Services.Store.AppStore.Import(json);
                // Windows-only: records from Android app sources are not imported (the file is not modified).
                var skippedAndroid = allApps.Where(a => ObtainiumPC.Sources.SourceCatalog.IsAndroidOnlyApp(a.Url, a.OverrideSource, App.Settings)).ToList();
                var apps = allApps.Except(skippedAndroid).ToList();
                var added = 0;
                var replaced = 0;
                foreach (var app in apps)
                {
                    if (App.UpsertApp(app)) replaced++; else added++;
                }
                await App.SaveAppsAsync();
                var settingsNote = hasSettings
                    ? " The file also contains settings; applying them lands with the settings import step."
                    : "";
                if (skippedAndroid.Count > 0)
                    settingsNote += "\n" + string.Format(Services.Localization.Loc.T("Skipped {0} Android apps (not supported): {1}"),
                        skippedAndroid.Count, string.Join(", ", skippedAndroid.Select(a => a.FinalName)));
                ShowResult(InfoBarSeverity.Success, "Import complete",
                    string.Format(Services.Localization.Loc.T("Added: {0}; replaced: {1}."), added, replaced) + settingsNote);
            }
            catch (Services.Store.ObtainiumImportException ex)
            {
                ShowResult(InfoBarSeverity.Error, "Import failed", ex.Message);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogger.Error("Import failed", ex);
                ShowResult(InfoBarSeverity.Error, "Import failed",
                    Services.Localization.Loc.T("The request failed. Details are in the logs."));
            }
        }

        // lib/pages/home.dart interpretLink app/apps deep-link: add app(s)
        // from raw JSON. The OS deep-link itself is Android/protocol-handler
        // territory; the paste box is the Windows entry point. A bare app
        // object or array is wrapped in the export envelope, like the
        // deep-link handler does with jsonEncode({'apps': ...}).
        private async void ImportPasteJson_Click(object sender, RoutedEventArgs e)
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 220,
                PlaceholderText = "{\"apps\": [ ... ]} or a single app object",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            };
            var dialog = new ThemedContentDialog
            {
                Title = Services.Localization.Loc.T("Paste raw JSON"),
                Content = new ScrollViewer { Content = box, MaxHeight = 320, Width = 480 },
                PrimaryButtonText = Services.Localization.Loc.T("Import"),
                CloseButtonText = Services.Localization.Loc.T("Cancel"),
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    text = "{\"apps\": " + text + "}";
                else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                         && !doc.RootElement.TryGetProperty("apps", out _))
                    text = "{\"apps\": [" + text + "]}";
            }
            catch (System.Text.Json.JsonException ex)
            {
                Services.Logging.AppLogger.Error("Pasted JSON could not be parsed", ex);
                ShowResult(InfoBarSeverity.Error, Services.Localization.Loc.T("Import failed"), Services.Localization.Loc.T("Invalid input"));
                return;
            }
            await ImportJsonAsync(text);
        }

        // lib/pages/import_export.dart _runDownloadsImport: lists the export
        // files in the app's downloads folder and imports the one picked.
        private async void ImportDownloads_Click(object sender, RoutedEventArgs e)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "ObtainiumPC");
            List<System.IO.FileInfo> candidates;
            try
            {
                candidates = System.IO.Directory.Exists(dir)
                    ? new System.IO.DirectoryInfo(dir).GetFiles("*.json")
                        .OrderByDescending(f => f.LastWriteTime).Take(20).ToList()
                    : new List<System.IO.FileInfo>();
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogger.Error("Listing downloads failed", ex);
                ShowResult(InfoBarSeverity.Error, "Import failed",
                    Services.Localization.Loc.T("The request failed. Details are in the logs."));
                return;
            }
            if (candidates.Count == 0)
            {
                ShowResult(InfoBarSeverity.Informational, "Nothing to import",
                    "No export files found in " + dir + ".");
                return;
            }
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
            foreach (var f in candidates)
                list.Items.Add(f.Name + "  (" + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ")");
            list.SelectedIndex = 0;
            var dialog = new ThemedContentDialog
            {
                Title = "Import from downloads",
                Content = list,
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var chosen = candidates[list.SelectedIndex >= 0 ? list.SelectedIndex : 0];
            await ImportJsonAsync(await System.IO.File.ReadAllTextAsync(chosen.FullName));
        }
    }
}
