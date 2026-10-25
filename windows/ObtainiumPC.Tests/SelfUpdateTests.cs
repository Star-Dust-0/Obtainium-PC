using ObtainiumPC.Models;
using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class SelfUpdateTests
{
    [Fact]
    public void First_run_registers_own_app_with_running_version_and_installer_filter()
    {
        var apps = new List<TrackedApp>();
        var added = SelfUpdate.EnsureTracked(apps, "0.1.0");
        Assert.NotNull(added);
        Assert.Single(apps);
        Assert.Equal(SelfUpdate.OwnUrl, added!.Url);
        Assert.Equal("0.1.0", added.InstalledVersion);
        Assert.Equal("manual", added.InstalledVersionSource);
        Assert.Equal("Setup", added.GetSettingString("apkFilterRegEx"));
        Assert.True(added.GetSettingBool("versionDetection"));
        Assert.True(added.GetSettingBool("includePrereleases"));
    }

    [Fact]
    public void Registration_is_idempotent_and_matches_existing_by_url_or_id()
    {
        var byUrl = new List<TrackedApp> { new() { Id = "x", Url = SelfUpdate.OwnUrl + "/" } };
        Assert.Null(SelfUpdate.EnsureTracked(byUrl, "0.1.0"));
        Assert.Single(byUrl);
        Assert.Null(byUrl[0].InstalledVersion);
        var byId = new List<TrackedApp> { new() { Id = SelfUpdate.OwnId, Url = "https://example.test/x" } };
        Assert.Null(SelfUpdate.EnsureTracked(byId, "0.1.0"));
    }

    [Fact]
    public void Os_detected_install_records_os_provenance()
    {
        var apps = new List<TrackedApp>();
        var added = SelfUpdate.EnsureTracked(apps, "0.1.0", "Obtainium-PC");
        Assert.Equal("os", added!.InstalledVersionSource);
        Assert.Equal("Obtainium-PC", added.OsMatchedName);
    }

    [Fact]
    public void Own_app_is_ordered_last_and_others_keep_order()
    {
        var a = new TrackedApp { Id = "a", Url = "https://x.test/a" };
        var self = new TrackedApp { Id = SelfUpdate.OwnId, Url = SelfUpdate.OwnUrl };
        var b = new TrackedApp { Id = "b", Url = "https://x.test/b" };
        var ordered = SelfUpdate.OrderSelfLast(new[] { a, self, b }, x => x);
        Assert.Equal(new[] { "a", "b", SelfUpdate.OwnId }, ordered.Select(x => x.Id));
    }
}
