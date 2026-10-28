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
    public void Legacy_record_is_recognised_and_moved_to_new_repository()
    {
        var legacy = new TrackedApp { Id = SelfUpdate.LegacyOwnId, Url = SelfUpdate.LegacyOwnUrl, Author = "MohamedElnaggar00" };
        var apps = new List<TrackedApp> { legacy };
        Assert.True(SelfUpdate.IsSelf(legacy));
        Assert.Null(SelfUpdate.EnsureTracked(apps, "0.1.0"));
        Assert.True(SelfUpdate.MigrateOwnRecord(apps));
        Assert.Equal(SelfUpdate.OwnUrl, legacy.Url);
        Assert.Equal(SelfUpdate.OwnAuthor, legacy.Author);
        Assert.False(SelfUpdate.MigrateOwnRecord(apps));
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
    public void Migration_adds_includePrereleases_only_when_absent_and_keeps_other_settings()
    {
        var old = new TrackedApp { Id = SelfUpdate.OwnId, Url = SelfUpdate.OwnUrl };
        old.SetSetting("apkFilterRegEx", "Setup");
        var optedOut = new TrackedApp { Id = "x", Url = SelfUpdate.OwnUrl + "/" };
        optedOut.SetSetting("includePrereleases", false);
        var other = new TrackedApp { Id = "o", Url = "https://x.test/o" };
        var apps = new List<TrackedApp> { old, optedOut, other };
        Assert.True(SelfUpdate.MigrateOwnRecord(apps));
        Assert.True(old.GetSettingBool("includePrereleases"));
        Assert.Equal("Setup", old.GetSettingString("apkFilterRegEx"));
        Assert.False(optedOut.GetSettingBool("includePrereleases", true));
        Assert.False(other.AdditionalSettings.ContainsKey("includePrereleases"));
        Assert.False(SelfUpdate.MigrateOwnRecord(apps));
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

public class SelfUpdateVersionEquivalenceTests
{
    [Theory]
    [InlineData("0.1.0", "v0.1", true)]   // the reported case: running 0.1.0, tag v0.1
    [InlineData("0.1.0", "v0.1.0", true)]
    [InlineData("0.1.0", "0.1", true)]
    [InlineData("0.1.0", "v0.1.1", false)]
    [InlineData("0.1.0", "v0.2", false)]
    [InlineData("0.1.0", "v1", false)]
    [InlineData("0.1.0", "v0.1-beta", false)]
    [InlineData("0.1.0", "v0.10", false)]
    [InlineData("0.1.0", "", false)]
    public void SameRelease_matches_only_the_same_numeric_release(string installed, string latest, bool expected)
        => Assert.Equal(expected, SelfUpdate.SameRelease(installed, latest));

    private static TrackedApp App(string url, string installed, string latest) =>
        new() { Id = "x", Url = url, Name = "x", InstalledVersion = installed, LatestVersion = latest };

    [Fact]
    public void Own_app_is_not_offered_itself_for_0_1_0_vs_v0_1()
        => Assert.False(SelfUpdate.IsUpdateable(App(SelfUpdate.OwnUrl, "0.1.0", "v0.1"), false));

    [Fact]
    public void Own_app_still_sees_a_real_newer_release()
        => Assert.True(SelfUpdate.IsUpdateable(App(SelfUpdate.OwnUrl, "0.1.0", "v0.1.1"), false));

    [Fact]
    public void Other_apps_keep_the_exact_string_comparison()
        => Assert.True(SelfUpdate.IsUpdateable(App("https://github.com/someone/else", "0.1.0", "v0.1"), false));
}

public class SelfUpdateDerivedDecisionTests
{
    private static TrackedApp A(string url, string? installed, string? latest) =>
        new() { Id = "x", Url = url, Name = "x", InstalledVersion = installed, LatestVersion = latest };

    [Fact]
    public void Own_app_detail_action_is_off_for_same_release_spelling()
        => Assert.False(SelfUpdate.DetailAction(A(SelfUpdate.OwnUrl, "0.1.0", "v0.1")).Enabled);

    [Fact]
    public void Own_app_detail_action_is_on_for_a_real_newer_release()
        => Assert.True(SelfUpdate.DetailAction(A(SelfUpdate.OwnUrl, "0.1.0", "v0.1.1")).Enabled);

    [Fact]
    public void Other_apps_detail_action_stays_exact()
        => Assert.True(SelfUpdate.DetailAction(A("https://github.com/a/b", "0.1.0", "v0.1")).Enabled);

    [Fact]
    public void First_install_is_unchanged_for_the_own_app()
    {
        var (enabled, first) = SelfUpdate.DetailAction(A(SelfUpdate.OwnUrl, null, "v0.1"));
        Assert.True(enabled); Assert.True(first);
    }

    [Fact]
    public void VersionsMatch_follows_the_same_rule_as_the_list()
    {
        Assert.True(SelfUpdate.VersionsMatch(A(SelfUpdate.OwnUrl, "0.1.0", "v0.1")));
        Assert.False(SelfUpdate.VersionsMatch(A("https://github.com/a/b", "0.1.0", "v0.1")));
        Assert.False(SelfUpdate.VersionsMatch(A(SelfUpdate.OwnUrl, "0.1.0", "v0.2")));
    }

    [Fact]
    public void Stored_versions_are_not_rewritten()
    {
        var app = A(SelfUpdate.OwnUrl, "0.1.0", "v0.1");
        _ = SelfUpdate.IsUpdateable(app, false); _ = SelfUpdate.DetailAction(app); _ = SelfUpdate.VersionsMatch(app);
        Assert.Equal("0.1.0", app.InstalledVersion); Assert.Equal("v0.1", app.LatestVersion);
    }
}
