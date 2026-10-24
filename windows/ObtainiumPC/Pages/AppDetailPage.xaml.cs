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

        private async Task<int> PickAssetAsync(IReadOnlyList<KeyValuePair<string, string>> assets)
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
            radios[0].IsChecked = true;
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
            var assets = _app.AssetUrls;
            if (assets.Count == 0)
            {
                Show(InfoBarSeverity.Warning, "No downloadable asset on the latest release.");
                return;
            }
            var index = _app.PreferredAssetIndex >= 0 && _app.PreferredAssetIndex < assets.Count ? _app.PreferredAssetIndex : 0;
            // Upstream neverAutoSelect: the user picks the asset every time.
            SourceCatalog.TryResolve(_app.Url, App.Settings, out var preSource);
            if (preSource?.NeverAutoSelect == true && assets.Count > 1)
            {
                var picked = await PickAssetAsync(assets);
                if (picked < 0) return;
                index = picked;
            }
            var asset = assets[index];
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
