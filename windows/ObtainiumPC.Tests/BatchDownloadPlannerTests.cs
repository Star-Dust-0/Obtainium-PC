using ObtainiumPC.Services.Download;
using Xunit;

namespace ObtainiumPC.Tests;

/// BatchDownloadPlanner: pure decisions behind the batch download flow.
/// Upstream refs: apps.dart:283, apps_provider_install.dart:1205/:802.
public class BatchDownloadPlannerTests
{
    [Theory]
    [InlineData(3, false, true)]   // default: confirm the bulk set
    [InlineData(3, true, false)]   // skipBulkUpdateConfirmation on
    [InlineData(1, false, true)]   // single app still confirms upstream
    [InlineData(0, false, false)]  // nothing to confirm
    public void Bulk_confirmation_follows_skip_setting(int count, bool skip, bool expected)
        => Assert.Equal(expected, BatchDownloadPlanner.NeedsBulkConfirmation(count, skip));

    [Theory]
    [InlineData(3, true, true)]    // parallel allowed, several picks
    [InlineData(3, false, false)]  // parallelDownloads off -> serial
    [InlineData(1, true, false)]   // one download never needs parallelism
    [InlineData(0, true, false)]
    public void Parallel_only_when_allowed_and_useful(int picks, bool parallel, bool expected)
        => Assert.Equal(expected, BatchDownloadPlanner.UseParallelDownloads(picks, parallel));

    [Theory]
    // proven downgrades block only when the setting is on (default on)
    [InlineData(true, "1.7.3", "1.7.2", true)]
    [InlineData(false, "1.7.3", "1.7.2", false)]
    // normal updates and equal versions never block
    [InlineData(true, "1.7.2", "1.7.3", false)]
    [InlineData(true, "1.7.3", "1.7.3", false)]
    // unknown/incomparable versions can never be proven downgrades
    [InlineData(true, null, "1.7.3", false)]
    [InlineData(true, "1.7.3", null, false)]
    [InlineData(true, "release-final", "1.7.2", false)]
    public void Downgrade_blocks_only_proven_downgrades_when_enabled(
        bool showError, string? installed, string? latest, bool expected)
        => Assert.Equal(expected, BatchDownloadPlanner.ShouldBlockDownload(showError, installed, latest));

    [Theory]
    // Windows containers launch the installer...
    [InlineData(true, "app-setup.exe", true)]
    [InlineData(true, "app.msixbundle", true)]
    [InlineData(null, "app.msi", true)]      // null = source has no opinion -> installable
    // ...Android packages and non-containers never do...
    [InlineData(true, "app-release.apk", false)]
    [InlineData(true, "checksums.txt", false)]
    // ...and a source that cannot install never launches.
    [InlineData(false, "app-setup.exe", false)]
    public void Installer_launches_only_for_installable_sources_and_containers(
        bool? supportsInstall, string asset, bool expected)
        => Assert.Equal(expected, BatchDownloadPlanner.ShouldLaunchInstaller(supportsInstall, asset));
}
