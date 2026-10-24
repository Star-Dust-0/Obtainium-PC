using ObtainiumPC.Services.Versioning;
using Xunit;

namespace ObtainiumPC.Tests;

public class VersionServiceTests
{
    [Fact]
    public void Null_regex_returns_null()
    {
        Assert.Null(VersionService.ExtractVersion(null, null, "app-1.2.3-release"));
    }

    [Fact]
    public void Default_match_group_is_the_whole_match()
    {
        Assert.Equal("v2.0.1", VersionService.ExtractVersion("v([0-9.]+)", null, "v2.0.1"));
    }

    [Fact]
    public void Numeric_match_group_selects_capture_group()
    {
        Assert.Equal("2.0.1", VersionService.ExtractVersion("v([0-9.]+)", "1", "v2.0.1"));
    }

    [Fact]
    public void Dollar_references_compose_groups()
    {
        Assert.Equal("7.3", VersionService.ExtractVersion(@"release-(\d+)-(\d+)", "$2.$1", "release-3-7"));
    }

    [Fact]
    public void No_match_throws()
    {
        Assert.Throws<NoVersionException>(() => VersionService.ExtractVersion(@"build-(\d+)", null, "no digits here"));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.10", -1)]
    [InlineData("2.0", "2.0.0", 0)]
    [InlineData("1.10.0", "1.9.9", 1)]
    public void CompareVersionsNumerically_orders_correctly(string v1, string v2, int expectedSign)
    {
        var result = VersionService.CompareVersionsNumerically(v1, v2);
        Assert.NotNull(result);
        Assert.Equal(expectedSign, Math.Sign(result!.Value));
    }
}

public class DetailActionTests
{
    [Fact]
    public void First_install_is_enabled_and_marked_install()
    {
        var (enabled, firstInstall) = ObtainiumPC.Services.Versioning.VersionService.DetailAction(null, "v8.9.8.1");
        Assert.True(enabled);
        Assert.True(firstInstall);
    }

    [Fact]
    public void Matching_versions_disable_the_action()
    {
        var (enabled, _) = ObtainiumPC.Services.Versioning.VersionService.DetailAction("v8.9.8.1", "v8.9.8.1");
        Assert.False(enabled);
    }

    [Fact]
    public void Differing_versions_enable_update_not_install()
    {
        var (enabled, firstInstall) = ObtainiumPC.Services.Versioning.VersionService.DetailAction("8.0", "v8.9.8.1");
        Assert.True(enabled);
        Assert.False(firstInstall);
    }

    [Fact]
    public void Unknown_latest_disables_the_action()
    {
        var (enabled, _) = ObtainiumPC.Services.Versioning.VersionService.DetailAction(null, null);
        Assert.False(enabled);
    }
    // Regression (test/version_reconciliation_test.dart "collapses a leading v
    // in both directions"): a registry version without the tag's leading "v"
    // is the same release - the tracked value collapses to the latest and no
    // update is offered. This is the Notepad++ 8.9.8.1 vs v8.9.8.1 case.
    [Fact]
    public void Reconcile_collapses_leading_v_real_to_latest()
    {
        var corrected = VersionService.ReconcileTrackedVersion(
            "2026.03", "2026.03", "v2026.03",
            versionDetectionIsStandard: true, naiveStandardVersionDetection: false);
        Assert.Equal("v2026.03", corrected);
    }

    [Fact]
    public void Reconcile_collapses_leading_v_tracked_to_latest()
    {
        var corrected = VersionService.ReconcileTrackedVersion(
            "v2026.03", "v2026.03", "2026.03",
            versionDetectionIsStandard: true, naiveStandardVersionDetection: false);
        Assert.Equal("2026.03", corrected);
    }

    [Fact]
    public void Reconcile_collapses_packaging_suffix_on_installed_side()
    {
        var corrected = VersionService.ReconcileTrackedVersion(
            "1.6.15-debug", "1.6.15-debug", "1.6.15",
            versionDetectionIsStandard: true, naiveStandardVersionDetection: false);
        Assert.Equal("1.6.15", corrected);
    }

    [Fact]
    public void Reconcile_adopts_real_when_nothing_tracked_then_collapse()
    {
        // Caller adopts real first (installed was null), then reconciliation
        // collapses the cosmetic diff - the npp 8.9.8.1 -> v8.9.8.1 flow.
        var adopted = "8.9.8.1";
        var corrected = VersionService.ReconcileTrackedVersion(
            adopted, "8.9.8.1", "v8.9.8.1",
            versionDetectionIsStandard: true, naiveStandardVersionDetection: false);
        Assert.Equal("v8.9.8.1", corrected);
    }

    [Fact]
    public void No_update_shown_after_cosmetic_collapse()
    {
        var corrected = VersionService.ReconcileTrackedVersion(
            "8.9.8.1", "8.9.8.1", "v8.9.8.1",
            versionDetectionIsStandard: true, naiveStandardVersionDetection: false);
        Assert.Equal("v8.9.8.1", corrected);
        // After the collapse, installed == latest, so no update and the
        // detail action button is disabled (upstream hasAction).
        var action = VersionService.DetailAction(corrected, "v8.9.8.1");
        Assert.False(action.Enabled);
    }

}
