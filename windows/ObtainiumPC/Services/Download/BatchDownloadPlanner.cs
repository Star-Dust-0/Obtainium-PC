using ObtainiumPC.Sources;
using ObtainiumPC.Services.Versioning;

namespace ObtainiumPC.Services.Download;

/// <summary>
/// Pure batch-download decisions behind AppsPage.BatchDownload_Click and the
/// detail-page download gate, unit-tested without WinUI. Upstream refs:
/// lib/pages/apps.dart:283 (bulk confirmation),
/// lib/providers/apps_provider_install.dart:1205 (parallelDownloads),
/// lib/providers/apps_provider_install.dart:802 (showAppDowngradeError).
/// </summary>
public static class BatchDownloadPlanner
{
    /// apps.dart:283 - the bulk confirmation shows unless skipped.
    public static bool NeedsBulkConfirmation(int candidateCount, bool skipBulkUpdateConfirmation)
        => candidateCount > 0 && !skipBulkUpdateConfirmation;

    /// apps_provider_install.dart:1205 - parallel only when allowed and there
    /// is more than one download.
    public static bool UseParallelDownloads(int pickCount, bool parallelDownloads)
        => parallelDownloads && pickCount > 1;

    /// A downgrade is only provable when both versions compare numerically;
    /// incomparable formats pass through (never a silent block).
    public static bool IsProvenDowngrade(string? installedVersion, string? latestVersion)
        => installedVersion != null
           && latestVersion != null
           && VersionService.CompareVersionsNumerically(installedVersion, latestVersion) > 0;

    /// apps_provider_install.dart:802 - default on; blocks only proven
    /// downgrades.
    public static bool ShouldBlockDownload(bool showAppDowngradeError, string? installedVersion, string? latestVersion)
        => showAppDowngradeError && IsProvenDowngrade(installedVersion, latestVersion);

    /// Per-asset installability gate: a source that cannot install, or an
    /// asset that is not a Windows app/container (e.g. an .apk), never
    /// reaches the installer. The asset stays downloadable for manual
    /// transfer.
    public static bool ShouldLaunchInstaller(bool? sourceSupportsInstall, string assetKey)
        => sourceSupportsInstall != false && AssetFilterService.IsAppOrContainerFile(assetKey);
}
