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
            Loaded += (_, _) =>
            {
                Services.Localization.Localizer.Apply(this);
                // The Back button is icon-only like upstream; its accessible
                // name and tooltip carry the localized string (Flutter
                // MaterialLocalizations backButtonTooltip, mirrored into
                // LocTranslations by the generator).
                var back = Services.Localization.Loc.T("Back");
                BackButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, back);
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(BackButton, back);
            };
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Services.Download.DownloadRegistry.ListChanged -= OnRegistryListChanged;
            Services.Download.DownloadRegistry.ListChanged += OnRegistryListChanged;
            await App.InitAsync();
            var id = e.Parameter as string;
            _app = App.TrackedApps.FirstOrDefault(a => a.Id == id);
            if (_app == null)
            {
                LeaveDetail();
                return;
            }
            // Embedded in the Apps split view the list is already visible: no Back button
            // until a sub-page (options) is pushed on this pane's own frame.
            BackButton.Visibility = IsEmbedded ? Visibility.Collapsed : Visibility.Visible;
            Render();
            // lib/pages/app.dart:1252 checkUpdateOnDetailPage (default false):
            // opening the detail page refreshes this app when the setting is on.
            if (App.Settings.CheckUpdateOnDetailPage)
                _ = RefreshThisAppAsync();
        }

        private async System.Threading.Tasks.Task RefreshThisAppAsync()
        {
            try
            {
                await App.Updater.CheckAppAsync(_app.Url, _app.AdditionalSettings, _app);
                await App.SaveAppsAsync();
                Render();
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogger.Warn($"Detail-page update check failed for {_app.FinalName}", ex);
            }
        }

        private async void SourceLink_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(_app?.Url, UriKind.Absolute, out var u))
                await Windows.System.Launcher.LaunchUriAsync(u);
        }

        private string? _sizeProbeKey;

        private static string FormatSize(long bytes) =>
            bytes >= 1048576 ? $"{bytes / 1048576.0:F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        // lib/pages/app.dart _maybeProbeDownloadSize: best-effort, never blocks the page.
        private async System.Threading.Tasks.Task ProbeSizeAsync(string assetUrl)
        {
            var app = _app;
            if (app == null) return;
            var version = app.LatestVersion;
            try
            {
                var url = assetUrl;
                if (SourceCatalog.TryResolve(app.Url, App.Settings, out var src) && src != null)
                    url = await src.AssetUrlPrefetchModifierAsync(url, app.Url, app.AdditionalSettings);
                var pat = url.Contains("github.com") || url.Contains("githubusercontent.com") ? App.Settings.GitHubPersonalAccessToken : null;
                var headers = src?.GetDownloadRequestHeaders(url, app.Url, app.AdditionalSettings);
                var size = await Services.Download.DownloadService.ProbeSizeAsync(url, pat, headers);
                // Ignore a result for a different release or app than the one now shown.
                if (size == null || _app != app || app.LatestVersion != version) return;
                app.RecordAssetSize(size.Value, assetUrl);
                await App.SaveAppsAsync();
                Render();
            }
            catch (Exception e) { Services.Logging.AppLogger.Info("Size probe failed: " + e.Message); }
        }

        private void Render()
        {
            if (_app == null) return;
            NameText.Text = _app.FinalName;
            AppIconImage.Source = Services.AppIconService.Source(_app);
            AuthorText.Text = _app.FinalAuthor;
            InstalledText.Text = Services.Localization.Loc.T("Installed: ") + (_app.InstalledVersion ?? Services.Localization.Loc.T("not installed"));
            LatestText.Text = Services.Localization.Loc.T("Latest: ") + (_app.LatestVersion ?? Services.Localization.Loc.T("unknown"));
            ReleaseDateText.Text = _app.ReleaseDate is { } rd ? Services.Localization.Loc.T("Released: ") + rd.ToString("yyyy-MM-dd") : "";
            // lib/pages/app.dart + app_list_tile.dart getChangeLogFn: the release
            // date becomes a tappable "changes" link when a changelog or release
            // URL exists.
            var hasChangeLog = _app.ChangeLog is { Length: > 0 }
                || !string.IsNullOrEmpty(_app.ReleaseUrl);
            ReleaseDateLink.Visibility = hasChangeLog && _app.ReleaseDate != null
                ? Visibility.Visible : Visibility.Collapsed;
            // Download size: shown only when known for the exact release and file that would be
            // downloaded (never a size saved for an older release or another file).
            var sizeUrl = _app.AssetUrls.Count == 0 ? null
                : _app.AssetUrls[Math.Clamp(_app.PreferredAssetIndex, 0, _app.AssetUrls.Count - 1)].Value;
            var known = sizeUrl == null ? null : _app.KnownSizeFor(sizeUrl);
            SizeText.Text = known is { } sz ? Services.Localization.Loc.T("Download size: ") + FormatSize(sz) : "";
            SizeText.Visibility = known != null ? Visibility.Visible : Visibility.Collapsed;
            if (known == null && sizeUrl != null && App.Settings.CheckUpdateOnDetailPage && _sizeProbeKey != Models.TrackedApp.SizeIdentity(_app.LatestVersion, sizeUrl))
            {
                _sizeProbeKey = Models.TrackedApp.SizeIdentity(_app.LatestVersion, sizeUrl);
                _ = ProbeSizeAsync(sizeUrl);
            }
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
            // lib/pages/app.dart _buildVersionInfoSections / _buildSourceInfoSections.
            LastCheckText.Text = Services.Localization.Loc.T("Last update check: {}").Replace("{}",
                _app.LastUpdateCheck is { } luc ? luc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : Services.Localization.Loc.T("Never"));
            SourceLinkText.Text = _app.Url ?? "";
            SourceIdText.Text = _app.Id ?? "";
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
            // lib/pages/app.dart hasAction + Install/Update label. Two
            // pre-templated buttons swapped by Visibility: a programmatic
            // Label change on an already-templated AppBarButton renders stale
            // text once the button has been disabled (ui-smoke 40139280718:
            // UIA name "Update", pixels "Install" - persisting even across
            // visibility toggles and remove/reinsert). Baked-in labels cannot
            // go stale; both buttons share Update_Click.
            var (actionEnabled, firstInstall) = Services.Versioning.VersionService.DetailAction(
                _app.InstalledVersion, _app.LatestVersion);
            // Track-only apps never install from here: the flow is a manual
            // download, so a third pre-templated button says Download instead
            // of Install/Update (an enabled "Install" promise is misleading -
            // upstream labels the mark action markInstalled/markUpdated, but
            // the port keeps the authorized manual-download flow).
            // Windows-only: legacy Android records are not updated or installed from here.
            if (App.Updater.IsLegacyUnsupported(_app)) actionEnabled = false;
            UpdateButton.IsEnabled = actionEnabled;
            InstallButton.IsEnabled = actionEnabled;
            // Manual download stays available for track-only apps even after
            // marking: the mark is version bookkeeping only and does not put
            // the file on disk.
            DownloadButton.IsEnabled = true;
            UpdateButton.Visibility = !trackOnlyApp && !firstInstall ? Visibility.Visible : Visibility.Collapsed;
            InstallButton.Visibility = !trackOnlyApp && firstInstall ? Visibility.Visible : Visibility.Collapsed;
            DownloadButton.Visibility = trackOnlyApp ? Visibility.Visible : Visibility.Collapsed;
            AttachInFlightDownload();
            // lib/pages/app.dart:508/511: track-only apps also keep the upstream
            // markInstalled/markUpdated bookkeeping action (records
            // installedVersion = latestVersion, no download, no install). Same
            // hasAction gating: disabled once the recorded install matches
            // latest; reset-install takes over from there (lib/pages/app.dart:600).
            // Disabled-looking mark states use the baked-disabled twin with
            // an explicitly pinned disabled Foreground: a freshly-shown
            // button keeps the enabled visual state regardless of when
            // IsEnabled was set (stale CommonStates on first template, same
            // family as 40139280718; 28h pixel review on run 40186712354).
            // The actionable
            // variants are enabled by construction and only show when
            // actionEnabled; MarkTrackOnly_Click still no-ops when the
            // recorded install matches latest.
            MarkInstalledAction.Visibility = trackOnlyApp && firstInstall && actionEnabled ? Visibility.Visible : Visibility.Collapsed;
            MarkUpdatedAction.Visibility = trackOnlyApp && !firstInstall && actionEnabled ? Visibility.Visible : Visibility.Collapsed;
            MarkUpdatedDisabledAction.Visibility = trackOnlyApp && !firstInstall && !actionEnabled ? Visibility.Visible : Visibility.Collapsed;
            // Windows has a per-app settings page only for packaged (MSIX) apps.
            // Until package detection lands, this stays honestly unavailable.
            OsSettingsButton.IsEnabled = false;
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
            var dialog = new ThemedContentDialog
            {
                Title = Services.Localization.Loc.T("Choose a file"),
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = Services.Localization.Loc.T("Download"),
                CloseButtonText = Services.Localization.Loc.T("Cancel"),
                XamlRoot = XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? selected : -1;
        }

        private Services.Download.DownloadEntry? _attached;
        private Services.Download.DownloadEntry? _renderedInstalled;

        /// Opened again while this app is downloading: show the live progress and keep the action off.
        private void AttachInFlightDownload()
        {
            if (_app == null) return;
            var entry = Services.Download.DownloadRegistry.Get(_app.Id);
            if (entry == null) return;
            UpdateButton.IsEnabled = false;
            InstallButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            DownloadPanel.Visibility = Visibility.Visible;
            // A new attempt (e.g. Retry from the Downloads list) replaces the stale error/cancel banner.
            if (MessageBar.IsOpen && MessageBar.Severity is InfoBarSeverity.Error or InfoBarSeverity.Informational) MessageBar.IsOpen = false;
            ApplyDownloadProgress(entry);
            if (ReferenceEquals(_attached, entry)) return;
            DetachInFlightDownload();
            _attached = entry;
            entry.Changed += OnAttachedChanged;
            entry.Ended += OnAttachedEnded;
        }

        private void DetachInFlightDownload()
        {
            if (_attached == null) return;
            _attached.Changed -= OnAttachedChanged;
            _attached.Ended -= OnAttachedEnded;
            _attached = null;
        }

        private void OnAttachedChanged()
        {
            var entry = _attached;
            if (entry != null) DispatcherQueue.TryEnqueue(() => ApplyDownloadProgress(entry));
        }

        private void OnAttachedEnded()
        {
            DispatcherQueue.TryEnqueue(() => { DetachInFlightDownload(); if (_app != null) Render(); });
        }

        private void ApplyDownloadProgress(Services.Download.DownloadEntry entry)
        {
            var (received, total) = entry.Snapshot;
            if (total is > 0)
            {
                DownloadBar.Value = Math.Min(100, (double)received / total.Value * 100);
                DownloadText.Text = $"{received / 1048576.0:F1} MB of {total.Value / 1048576.0:F1} MB";
            }
            else DownloadText.Text = $"{received / 1048576.0:F1} MB";
        }

        private void OnRegistryListChanged() => DispatcherQueue.TryEnqueue(() =>
        {
            if (_app != null && Services.Download.DownloadRegistry.Get(_app.Id) != null && !ReferenceEquals(_attached, Services.Download.DownloadRegistry.Get(_app.Id)))
                AttachInFlightDownload();
            // The independent install check confirmed the new version: show it (and the Update state) now.
            if (_app != null)
            {
                var done = Services.Download.DownloadRegistry.FinishedEntries().FirstOrDefault(x => x.AppId == _app.Id && x.State == Services.Download.DownloadState.Installed);
                if (done != null && !ReferenceEquals(_renderedInstalled, done)) { _renderedInstalled = done; Render(); }
            }
        });

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Services.Download.DownloadRegistry.ListChanged -= OnRegistryListChanged;
            DetachInFlightDownload();
            base.OnNavigatedFrom(e);
        }

        private bool IsEmbedded => Frame.Name == "DetailFrame";

        /// Single-pane: go back to the list. Split view: show the empty pane.
        private void LeaveDetail()
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else if (IsEmbedded) Frame.Navigate(typeof(EmptyDetailPage));
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_app != null && Services.Download.DownloadRegistry.Get(_app.Id) != null)
            {
                Show(InfoBarSeverity.Informational, Services.Localization.Loc.T("This app is already downloading."));
                return;
            }
            if (_app == null) return;
            // lib/providers/apps_provider_install.dart:802 showAppDowngradeError
            // (default true): block an update that would downgrade. Android
            // compares APK versionCodes; the port compares versions
            // numerically when comparable - incomparable versions cannot be
            // proven a downgrade and pass through.
            if (Services.Download.BatchDownloadPlanner.ShouldBlockDownload(
                App.Settings.ShowAppDowngradeError, _app.InstalledVersion, _app.LatestVersion))
            {
                var blocked = new ThemedContentDialog
                {
                    Title = Services.Localization.Loc.T("Downgrade blocked"),
                    Content = _app.FinalName + ": " + Services.Localization.Loc.T("the installed version") + " " + _app.InstalledVersion + " " + Services.Localization.Loc.T("is newer than the latest release") + " " + _app.LatestVersion + ". " + Services.Localization.Loc.T("The update was blocked."),
                    CloseButtonText = Services.Localization.Loc.T("OK"),
                    XamlRoot = XamlRoot,
                };
                await blocked.ShowAsync();
                return;
            }
            // Upstream confirmAppFileUrl(pickAnyAsset: true) offers
            // apkUrls + otherAssetUrls.
            var assets = _app.AssetUrls.ToList();
            foreach (var other in _app.OtherAssetUrls)
            {
                // The same file is listed in both groups; checksum files and
                // source snapshots are not installers, so keep them out of the picker.
                if (assets.Any(a => a.Value == other.Value || a.Key == other.Key)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(other.Key, @"(sha-?(1|256|512)|checksums?|\.(sig|asc|sha256|sha512|md5|txt))", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                if (other.Value.Contains("/tarball/") || other.Value.Contains("/zipball/")) continue;
                assets.Add(other);
            }
            if (assets.Count == 0)
            {
                Show(InfoBarSeverity.Warning, Services.Localization.Loc.T("No downloadable asset on the latest release."));
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
            var entry = Services.Download.DownloadRegistry.TryBegin(_app.Id, asset.Key, _app.FinalName);
            if (entry == null)
            {
                Show(InfoBarSeverity.Informational, Services.Localization.Loc.T("This app is already downloading."));
                return;
            }
            UpdateButton.IsEnabled = false;
            InstallButton.IsEnabled = false;
            DownloadPanel.Visibility = Visibility.Visible;
            var outcome = Services.Download.DownloadState.Failed;
            string? outcomeMessage = null;
            try
            {
                var ui = new Progress<(long Received, long? Total)>(p =>
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
                var fetched = await Services.Download.DownloadCoordinator.FetchAsync(_app, asset, entry, ui);
                var path = fetched.Path;
                var installable = fetched.Installable;
                outcome = installable ? Services.Download.DownloadState.InstallerOpened : Services.Download.DownloadState.Saved;
                if (installable)
                {
                    Services.Download.DownloadCoordinator.LaunchInstaller(_app, entry, path);
                    Show(InfoBarSeverity.Success, string.Format(Services.Localization.Loc.T("Downloaded {0} and opened the installer."), asset.Key));
                    if (CanDetectInstalled())
                    {
PostDownloadNote.Text = Services.Localization.Loc.T("Once the installer finishes, the installed version is detected automatically (registry, also on every update check). Use Detect installed version now to reconcile immediately; Mark as updated is the manual fallback when detection cannot find the app.");
                        DetectNowButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
PostDownloadNote.Text = Services.Localization.Loc.T("Automatic installed-version detection is not available for this app. After installing, use Mark as updated.");
                        DetectNowButton.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    Show(InfoBarSeverity.Success, Sources.AssetFilterService.IsAndroidPackage(asset.Key)
                        ? string.Format(Services.Localization.Loc.T("Downloaded {0}. Android package - cannot install on Windows; the file is saved for manual transfer."), asset.Key)
                        : string.Format(Services.Localization.Loc.T("Downloaded {0}. It is not a Windows installer, so it was not launched; the file is in your downloads folder."), asset.Key));
                    PostDownloadNote.Text = "";
                    DetectNowButton.Visibility = Visibility.Collapsed;
                }
                PostDownloadPanel.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException) when (entry.CancelRequested)
            {
                outcome = Services.Download.DownloadState.Cancelled;
                outcomeMessage = "Cancelled - partial file removed";
                Show(InfoBarSeverity.Informational, Services.Localization.Loc.T("Download cancelled. The partial file was removed."));
            }
            catch (Exception ex)
            {
                outcomeMessage = Services.Localization.SourceErrorText.Describe(ex, Services.Localization.Loc.T);
                Show(InfoBarSeverity.Error, Services.Localization.Loc.T("Download failed: ") + outcomeMessage);
            }
            finally
            {
                Services.Download.DownloadRegistry.End(entry, outcome, outcomeMessage);
                DownloadPanel.Visibility = Visibility.Collapsed;
                // Recompute instead of blindly re-enabling: if detection already
                // reconciled installed == latest, the action stays disabled.
                var postEnabled = Services.Versioning.VersionService
                    .DetailAction(_app.InstalledVersion, _app.LatestVersion).Enabled;
                UpdateButton.IsEnabled = postEnabled;
                InstallButton.IsEnabled = postEnabled;
            }
        }

        // lib/pages/app.dart: manual mark is the fallback for when OS-level
        // detection is unavailable or cannot match the app (upstream detects
        // from the Android package manager; this port detects from the
        // registry Uninstall entries).
        // Upstream track-only mark action: version bookkeeping only, no
        // download, no installer launch.
        private async void MarkTrackOnly_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            // Same hasAction gate as upstream: marking an app whose recorded
            // install already matches the latest version is a no-op.
            if (_app.InstalledVersion == _app.LatestVersion) return;
            _app.InstalledVersion = _app.LatestVersion;
            _app.InstalledVersionSource = "manual";
            _app.OsMatchedName = null;
            await App.SaveAppsAsync();
            Render();
        }

        private async void MarkUpdated_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            _app.InstalledVersion = _app.LatestVersion;
            _app.InstalledVersionSource = "manual";
            _app.OsMatchedName = null;
            await App.SaveAppsAsync();
            PostDownloadPanel.Visibility = Visibility.Collapsed;
            Render();
        }

        private bool CanDetectInstalled()
        {
            if (_app == null || _app.GetSettingBool("trackOnly")) return false;
            if (!_app.GetSettingBool("versionDetection", true)) return false;
            if (SourceCatalog.TryResolve(_app.Url, App.Settings, out var s) && s is { VersionDetectionDisallowed: true })
                return false;
            return true;
        }

        // Runs the same registry detection AppUpdateService applies on every
        // update check; only Strong/Exact matches are trusted.
        private async Task<bool> TryDetectInstalledAsync()
        {
            if (_app == null || !CanDetectInstalled()) return false;
            var match = Services.Detection.InstalledAppsService.FindMatch(_app.FinalName, _app.Author);
            if (match is not { Confidence: >= Services.Detection.InstalledAppsService.MatchConfidence.Strong })
                return false;
            if (string.IsNullOrEmpty(match.Info.DisplayVersion)) return false;
            _app.InstalledVersionSource = "os";
            _app.OsMatchedName = match.Info.DisplayName;
            var realInstalled = match.Info.DisplayVersion;
            var before = _app.InstalledVersion;
            if (before == null) _app.InstalledVersion = realInstalled;
            // Same reconciliation as the update-check path: collapse cosmetic
            // differences (leading "v") so the same release reads as installed.
            var corrected = Services.Versioning.VersionService.ReconcileTrackedVersion(
                _app.InstalledVersion, realInstalled, _app.LatestVersion ?? realInstalled,
                versionDetectionIsStandard: true,
                naiveStandardVersionDetection: false);
            if (corrected != null) _app.InstalledVersion = corrected;
            if (_app.InstalledVersion == before) return true;
            await App.SaveAppsAsync();
            return true;
        }

        private async void DetectNow_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            DetectNowButton.IsEnabled = false;
            try
            {
                if (await TryDetectInstalledAsync())
                {
                    Show(InfoBarSeverity.Success, string.Format(Services.Localization.Loc.T("Installed version detected: {0}"), _app.InstalledVersion));
                    PostDownloadPanel.Visibility = Visibility.Collapsed;
                    Render();
                }
                else
                {
Show(InfoBarSeverity.Warning, Services.Localization.Loc.T("No installed version could be detected. If the installer has finished, try again in a moment; otherwise install the app first."));
                }
            }
            finally
            {
                DetectNowButton.IsEnabled = true;
            }
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
            var dialog = new ThemedContentDialog
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
                Show(InfoBarSeverity.Error, Services.Localization.Loc.T("Update check failed on the new URL: ") + Services.Localization.SourceErrorText.Describe(ex, Services.Localization.Loc.T));
            }
            Render();
        }

        private async void ResetInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_app == null) return;
            // lib/pages/app.dart resetInstallStatus.
            _app.InstalledVersion = null;
            _app.InstalledVersionSource = null;
            _app.OsMatchedName = null;
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
            var dialog = new ThemedContentDialog
            {
                Title = string.Format(Services.Localization.Loc.T("Remove {0}?"), _app.FinalName),
                Content = Services.Localization.Loc.T("The app itself is not uninstalled; Obtainium-PC just stops tracking it."),
                PrimaryButtonText = Services.Localization.Loc.T("Remove"),
                CloseButtonText = Services.Localization.Loc.T("Cancel"),
                XamlRoot = this.XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                App.RemoveApp(_app);
                await App.SaveAppsAsync();
                LeaveDetail();
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
