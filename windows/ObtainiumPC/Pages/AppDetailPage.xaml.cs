using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Models;
using ObtainiumPC.Sources;
using ObtainiumPC.Services.Download;

namespace ObtainiumPC.Pages
{
    public sealed partial class AppDetailPage : Page
    {
        private TrackedApp? _app;

        public AppDetailPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            var id = e.Parameter as string;
            _app = App.TrackedApps.FirstOrDefault(a => a.Id == id);
            if (_app == null)
            {
                Frame.GoBack();
                return;
            }
            Render();
        }

        private void Render()
        {
            if (_app == null) return;
            NameText.Text = _app.FinalName;
            AuthorText.Text = _app.FinalAuthor;
            InstalledText.Text = "Installed: " + (_app.InstalledVersion ?? "not installed");
            LatestText.Text = "Latest: " + (_app.LatestVersion ?? "unknown");
            ReleaseDateText.Text = _app.ReleaseDate is { } rd ? "Released: " + rd.ToString("yyyy-MM-dd") : "";
            // lib/pages/app.dart + app_list_tile.dart getChangeLogFn: the release
            // date becomes a tappable "changes" link when a changelog or release
            // URL exists.
            var hasChangeLog = _app.ChangeLog is { Length: > 0 }
                || !string.IsNullOrEmpty(_app.ReleaseUrl);
            ReleaseDateLink.Visibility = hasChangeLog && _app.ReleaseDate != null
                ? Visibility.Visible : Visibility.Collapsed;
            ReleaseLink.Visibility = !string.IsNullOrEmpty(_app.ReleaseUrl) ? Visibility.Visible : Visibility.Collapsed;
            TrackButton.Label = _app.Disabled ? "Enable tracking" : "Disable tracking";
            // lib/pages/app.dart: reset is offered only for track-only apps or
            // non-standard version detection (not yet ported -> treated as
            // standard), when the recorded install matches the latest version.
            var trackOnly = _app.AdditionalSettings.TryGetValue("trackOnly", out var to)
                && to.ValueKind == System.Text.Json.JsonValueKind.True;
            ResetInstallButton.Visibility = trackOnly
                && _app.InstalledVersion != null
                && _app.InstalledVersion == _app.LatestVersion
                ? Visibility.Visible : Visibility.Collapsed;
            // lib/pages/app.dart _buildRepoRenameSection.
            if (_app.HasPendingRepoRename)
            {
                RenameUrlText.Text = _app.PendingRepoRenameUrl;
                RenameBar.IsOpen = true;
            }
            else
            {
                RenameBar.IsOpen = false;
            }
            // lib/pages/app.dart: update-health note linking to the health page.
            // lib/models/app.dart isVersionPseudo + app.dart detail notes.
            var trackOnlyApp = _app.AdditionalSettings.TryGetValue("trackOnly", out var toe)
                && toe.ValueKind == System.Text.Json.JsonValueKind.True;
            var versionDetection = !_app.AdditionalSettings.TryGetValue("versionDetection", out var vde)
                || vde.ValueKind != System.Text.Json.JsonValueKind.False;
            var pseudo = trackOnlyApp || (_app.InstalledVersion != null && !versionDetection);
            var notes = new System.Collections.Generic.List<string>();
            if (trackOnlyApp) notes.Add("This app is track-only.");
            if (pseudo) notes.Add("Pseudo-versioning is in use for this app.");
            VersionNoteText.Text = string.Join("\n", notes);
            VersionNoteText.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var healthIgnored = Services.Updates.UpdateHealthService.IsIgnored(_app);
            var healthIssues = Services.Updates.UpdateHealthService.HasIssues(_app, App.Settings);
            if (healthIssues || healthIgnored)
            {
                HealthLink.Content = healthIgnored ? "Update health checks ignored for this app" : "Update health: view details";
                HealthLink.Visibility = Visibility.Visible;
            }
            else
            {
                HealthLink.Visibility = Visibility.Collapsed;
            }
            // lib/pages/app.dart hasAction + Install/Update label.
            var (actionEnabled, firstInstall) = Services.Versioning.VersionService.DetailAction(
                _app.InstalledVersion, _app.LatestVersion);
            UpdateButton.IsEnabled = actionEnabled;
            if (actionEnabled && firstInstall)
            {
                UpdateButton.Label = "Install";
                ((Microsoft.UI.Xaml.Controls.FontIcon)UpdateButton.Icon).Glyph = "\uE8B5";
            }
            // Windows has a per-app settings page only for packaged (MSIX) apps.
            // Until package detection lands, this stays honestly unavailable.
            OsSettingsButton.IsEnabled = false;
            if (_app.ChangeLog is { Length: > 0 } log)
            {
                ChangeLogText.Text = log;
                ChangeLogPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ChangeLogPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<int> PickAssetAsync(IReadOnlyList<KeyValuePair<string, string>> assets, int preselect = 0)
        {
            var selected = -1;
            var panel = new StackPanel { Spacing = 4 };
            var radios = new List<RadioButton>();
            for (var i = 0; i < assets.Count; i++)
            {
                var radio = new RadioButton { Content = assets[i].Key, GroupName = "assetPick" };
                var captured = i;
                radio.Checked += (s, e) => selected = captured;
                radios.Add(radio);
                panel.Children.Add(radio);
            }
            radios[Math.Clamp(preselect, 0, radios.Count - 1)].IsChecked = true;
            var dialog = new ContentDialog
            {
                Title = "Choose a file",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = "Download",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? selected : -1;
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            // Upstream confirmAppFileUrl(pickAnyAsset: true) offers
            // apkUrls + otherAssetUrls.
            var assets = _app.AssetUrls.Concat(_app.OtherAssetUrls).ToList();
            if (assets.Count == 0)
            {
                Show(InfoBarSeverity.Warning, "No downloadable asset on the latest release.");
                return;
            }
            // Upstream confirmAppFileUrl(pickAnyAsset: true, evenIfSingleChoice:
            // true): a manual download always opens the asset picker. The
            // preferred index is preselected; when the app has an asset filter
            // regex, the first matching asset is preselected instead.
            var index = _app.PreferredAssetIndex >= 0 && _app.PreferredAssetIndex < assets.Count ? _app.PreferredAssetIndex : 0;
            var filterRe = _app.AdditionalSettings.TryGetValue("apkFilterRegEx", out var fr)
                && fr.ValueKind == System.Text.Json.JsonValueKind.String ? fr.GetString() : null;
            if (!string.IsNullOrEmpty(filterRe))
            {
                try
                {
                    var rx = new System.Text.RegularExpressions.Regex(filterRe);
                    var matchIdx = assets.FindIndex(a => rx.IsMatch(a.Key));
                    if (matchIdx >= 0) index = matchIdx;
                }
                catch { /* invalid regex: keep default preselection */ }
            }
            var picked = await PickAssetAsync(assets, index);
            if (picked < 0) return;
            var asset = assets[picked];
            UpdateButton.IsEnabled = false;
            DownloadPanel.Visibility = Visibility.Visible;
            try
            {
                var progress = new Progress<(long Received, long? Total)>(p =>
                {
                    if (p.Total is > 0)
                    {
                        DownloadBar.Value = Math.Min(100, (double)p.Received / p.Total.Value * 100);
                        DownloadText.Text = $"{p.Received / 1048576.0:F1} MB of {p.Total.Value / 1048576.0:F1} MB";
                    }
                    else
                    {
                        DownloadText.Text = $"{p.Received / 1048576.0:F1} MB";
                    }
                });
                var downloadUrl = asset.Value;
                if (SourceCatalog.TryResolve(_app.Url, App.Settings, out var dlSource) && dlSource != null)
                    downloadUrl = await dlSource.AssetUrlPrefetchModifierAsync(downloadUrl, _app.Url, _app.AdditionalSettings);
                // The GitHub PAT is only attached to GitHub asset requests.
                var gitHubPat = downloadUrl.Contains("github.com") || downloadUrl.Contains("githubusercontent.com")
                    ? App.Settings.GitHubPersonalAccessToken : null;
                var extraHeaders = dlSource?.GetDownloadRequestHeaders(downloadUrl, _app.Url, _app.AdditionalSettings);
                var path = await new DownloadService().DownloadAsync(
                    downloadUrl, asset.Key, progress, gitHubPat, extraHeaders);
                _app.AssetSizeBytes = new System.IO.FileInfo(path).Length;
                await App.SaveAppsAsync();
                var installable = dlSource?.SupportsInstall != false;
                if (installable)
                {
                    DownloadService.OpenInstaller(path);
                    Show(InfoBarSeverity.Success, $"Downloaded {asset.Key} and opened the installer. Use Mark as updated after installing.");
                }
                else
                {
                    Show(InfoBarSeverity.Success, $"Downloaded {asset.Key}. Android package - cannot install on Windows; the file is saved for manual transfer.");
                }
                MarkUpdatedButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Show(InfoBarSeverity.Error, "Download failed: " + ex.Message);
            }
            finally
            {
                DownloadPanel.Visibility = Visibility.Collapsed;
                UpdateButton.IsEnabled = true;
            }
        }

        private async void MarkUpdated_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            _app.InstalledVersion = _app.LatestVersion;
            await App.SaveAppsAsync();
            MarkUpdatedButton.Visibility = Visibility.Collapsed;
            Render();
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            if (_app != null) Frame.Navigate(typeof(AppOptionsPage), _app.Id);
        }

        private void OsSettings_Click(object sender, RoutedEventArgs e)
        {
            // Reserved for packaged (MSIX) installs: ms-settings:appsfeatures-app?<PFN>.
        }

        // lib/components/app_list_tile.dart showChangeLogDialog: a changelog that
        // is only a URL is shown as a link; otherwise it is rendered as markdown
        // (common subset - full flutter_markdown_plus parity disclosed in PARITY).
        private static readonly System.Text.RegularExpressions.Regex ChangelogUrlRegex =
            new(@"^(http|ftp|https)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private async void ReleaseDate_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            var changeLog = _app.ChangeLog?.Trim();
            string? changesUrl = null;
            if (!string.IsNullOrEmpty(changeLog) && ChangelogUrlRegex.IsMatch(changeLog))
            {
                changesUrl = changeLog;
                changeLog = null;
            }
            var panel = new StackPanel { Spacing = 12 };
            if (changesUrl != null)
            {
                var link = new HyperlinkButton { Content = changesUrl, NavigateUri = new Uri(changesUrl) };
                panel.Children.Add(link);
            }
            if (!string.IsNullOrEmpty(changeLog))
            {
                panel.Children.Add(new ScrollViewer
                {
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = Services.MarkdownRenderer.Render(changeLog),
                });
            }
            var dialog = new ContentDialog
            {
                Title = "Changes - " + _app.LatestVersion,
                Content = panel,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot,
            };
            await dialog.ShowAsync();
        }

        // lib/providers/apps_provider_install.dart updatePendingRepoRename(id, null)
        private void HealthLink_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(UpdateHealthPage));
        }

        private async void RenameDismiss_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            _app.PendingRepoRenameUrl = null;
            await App.SaveAppsAsync();
            Render();
        }

        // lib/providers/apps_provider_install.dart acceptRepoRename + getUpdate
        private async void RenameAccept_Click(object sender, RoutedEventArgs e)
        {
            if (_app?.PendingRepoRenameUrl is not { } newUrl) return;
            _app.Url = newUrl;
            _app.PendingRepoRenameUrl = null;
            await App.SaveAppsAsync();
            try
            {
                await App.Updater.CheckAppAsync(_app.Url, _app.AdditionalSettings, _app);
                await App.SaveAppsAsync();
            }
            catch (Exception ex)
            {
                Show(InfoBarSeverity.Error, "Update check failed on the new URL: " + ex.Message);
            }
            Render();
        }

        private async void ResetInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            // lib/pages/app.dart resetInstallStatus.
            _app.InstalledVersion = null;
            await App.SaveAppsAsync();
            Render();
        }

        private async void Track_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            _app.Disabled = !_app.Disabled;
            await App.SaveAppsAsync();
            Render();
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            var dialog = new ContentDialog
            {
                Title = "Remove " + _app.FinalName + "?",
                Content = "The app itself is not uninstalled; Obtainium-PC just stops tracking it.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                App.TrackedApps.Remove(_app);
                await App.SaveAppsAsync();
                Frame.GoBack();
            }
        }

        private async void ReleaseLink_Click(object sender, RoutedEventArgs e)
        {
            if (_app?.ReleaseUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }

        private void Show(InfoBarSeverity severity, string message)
        {
            MessageBar.Severity = severity;
            MessageBar.Message = message;
            MessageBar.IsOpen = true;
        }
    }
}
