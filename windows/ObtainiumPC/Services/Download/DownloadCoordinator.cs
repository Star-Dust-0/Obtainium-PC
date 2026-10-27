using System.Diagnostics;
using ObtainiumPC.Models;
using ObtainiumPC.Sources;

namespace ObtainiumPC.Services.Download;

public sealed record DownloadResult(string Path, bool Installable, DownloadService.ChecksumStatus Checksum, AppSourceBase? Source);

/// <summary>
/// The download pipeline for one tracked app and one chosen asset, independent of any page:
/// fetch (with progress, cancel and the release checksum), then launch the installer and
/// check the installed version on its own. Pages and the one-click Retry share it.
/// </summary>
public static class DownloadCoordinator
{
    public static async Task<DownloadResult> FetchAsync(TrackedApp app, KeyValuePair<string, string> asset,
        DownloadEntry entry, IProgress<(long Received, long? Total)>? ui = null)
    {
        entry.Tag = app; entry.Asset = asset;
        var url = asset.Value;
        SourceCatalog.TryResolve(app.Url, App.Settings, out var source);
        if (source != null) url = await source.AssetUrlPrefetchModifierAsync(url, app.Url, app.AdditionalSettings);
        // The GitHub PAT is only attached to GitHub asset requests.
        var pat = url.Contains("github.com") || url.Contains("githubusercontent.com") ? App.Settings.GitHubPersonalAccessToken : null;
        var headers = source?.GetDownloadRequestHeaders(url, app.Url, app.AdditionalSettings);
        var checksum = DownloadService.ChecksumStatus.NoChecksumPublished;
        // The release SHA-256 is checked on the .part file, before it can replace an earlier good download.
        Func<string, Task> checksumCheck = async part =>
            checksum = await DownloadService.VerifyAgainstReleaseChecksumAsync(part, asset.Key, app.AssetUrls.Concat(app.OtherAssetUrls));
        var progress = new Progress<(long Received, long? Total)>(p =>
        {
            DownloadRegistry.Report(entry, p.Received, p.Total);
            ui?.Report(p);
        });
        var path = await new DownloadService().DownloadAsync(url, asset.Key, progress, pat, headers, verifyBeforePromote: checksumCheck, ct: entry.Token);
        Logging.AppLogger.Info($"Download {asset.Key} for {app.FinalName}: SHA-256 {(checksum == DownloadService.ChecksumStatus.Verified ? "verified against the release checksum" : "NOT verified (" + checksum + ")")}");
        app.RecordAssetSize(new FileInfo(path).Length, asset.Value);
        await App.SaveAppsAsync();
        return new DownloadResult(path, BatchDownloadPlanner.ShouldLaunchInstaller(source?.SupportsInstall, asset.Key), checksum, source);
    }

    /// Launches the installer and, for every app but ourselves (our own installer closes this app),
    /// starts the independent installed-version check that finishes the job's state.
    public static void LaunchInstaller(TrackedApp app, DownloadEntry entry, string path, bool watch = true)
    {
        var proc = DownloadService.OpenInstaller(path);
        if (watch && !Updates.SelfUpdate.IsSelf(app)) _ = WatchInstallAsync(app, entry, proc);
    }

    internal static async Task WatchInstallAsync(TrackedApp app, DownloadEntry entry, Process? proc, bool quick = false)
    {
        var expected = app.LatestVersion; var previous = app.InstalledVersion;
        var deadline = DateTimeOffset.Now.AddMinutes(quick ? 0.4 : 15);
        var lastResult = InstallCheckResult.NotDetected; string? lastDetected = null;
        DateTimeOffset? endedAt = null; int? exit = null;
        try
        {
            while (DateTimeOffset.Now < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                if (endedAt == null && proc != null)
                {
                    try { if (proc.HasExited) { endedAt = DateTimeOffset.Now; exit = proc.ExitCode; } } catch { endedAt = DateTimeOffset.Now; }
                }
                string? detected = null;
                try
                {
                    var match = Detection.InstalledAppsService.FindMatch(app.FinalName, app.Author);
                    if (match is { Confidence: >= Detection.InstalledAppsService.MatchConfidence.Strong }) detected = match.Info.DisplayVersion;
                }
                catch { /* registry read failed: treated as not detected */ }
                lastDetected = detected;
                lastResult = InstallCheck.Evaluate(detected, expected, previous);
                if (lastResult == InstallCheckResult.Confirmed)
                {
                    app.InstalledVersionSource = "os";
                    var corrected = Versioning.VersionService.ReconcileTrackedVersion(app.InstalledVersion, detected!, expected ?? detected!, true, false);
                    app.InstalledVersion = corrected ?? detected;
                    await App.SaveAppsAsync();
                    entry.SetState(DownloadState.Installed, "version " + detected + " confirmed");
                    return;
                }
                // Installers that hand over to a child process exit early: allow a grace period after the exit.
                if (endedAt != null && DateTimeOffset.Now - endedAt > TimeSpan.FromSeconds(30)) break;
                // No process handle to watch: give up after 5 minutes instead of 15.
                if (proc == null && !quick && DateTimeOffset.Now > deadline.AddMinutes(-10)) break;
            }
        }
        catch (Exception ex) { Logging.AppLogger.Warn("install check failed", ex); }
        entry.SetState(DownloadState.InstallUnconfirmed, InstallCheck.UnconfirmedMessage(lastResult, lastDetected, exit, endedAt != null));
    }

    /// "Check again" on an unconfirmed job: a short fresh look at the installed version.
    public static void CheckAgain(DownloadEntry entry)
    {
        if (entry.Tag is not TrackedApp app) return;
        entry.SetState(DownloadState.InstallerOpened, null);
        _ = WatchInstallAsync(app, entry, null, quick: true);
    }

    /// One-click Retry of a failed or cancelled job: starts over from zero for the same app and file.
    public static async Task RetryAsync(DownloadEntry old)
    {
        if (old.Tag is not TrackedApp app || old.Asset is not { } asset) return;
        var entry = DownloadRegistry.TryBegin(app.Id, asset.Key, app.FinalName);
        if (entry == null) return;
        var outcome = DownloadState.Failed; string? message = null;
        try
        {
            var r = await FetchAsync(app, asset, entry);
            if (r.Installable) { outcome = DownloadState.InstallerOpened; LaunchInstaller(app, entry, r.Path); }
            else outcome = DownloadState.Saved;
        }
        catch (OperationCanceledException) when (entry.CancelRequested) { outcome = DownloadState.Cancelled; message = "Cancelled - partial file removed"; }
        catch (Exception ex) { message = Localization.SourceErrorText.Describe(ex, Localization.Loc.T); Logging.AppLogger.Warn("retry failed", ex); }
        finally { DownloadRegistry.End(entry, outcome, message); }
    }
}
