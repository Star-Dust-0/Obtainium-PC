using System.Text.Json;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class UpdateHealthServiceTests
{
    private static TrackedApp App(int failedChecks = 0, long? releaseDateUs = null, bool? archived = null, int? filtered = null) =>
        new()
        {
            Id = "test",
            Url = "https://github.com/a/b",
            Name = "Test",
            Author = "a",
            LatestVersion = "1.0",
            ConsecutiveFailedChecks = failedChecks,
            ReleaseDateUs = releaseDateUs,
            IsArchived = archived,
            NewerReleasesFilteredOut = filtered,
        };

    private static readonly AppSettings Defaults = new();

    [Fact]
    public void Failed_checks_flag_at_global_threshold()
    {
        var config = UpdateHealthService.ResolveConfig(new Dictionary<string, JsonElement>(), Defaults);
        var reasons = UpdateHealthService.EvaluateReasons(App(failedChecks: 5), config);
        Assert.Contains(UpdateHealthReason.FailedChecks, reasons);
        reasons = UpdateHealthService.EvaluateReasons(App(failedChecks: 4), config);
        Assert.DoesNotContain(UpdateHealthReason.FailedChecks, reasons);
    }

    [Fact]
    public void Per_app_off_override_disables_check()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["updateHealthFailedChecks"] = JsonSerializer.SerializeToElement("off"),
        };
        var config = UpdateHealthService.ResolveConfig(settings, Defaults);
        var reasons = UpdateHealthService.EvaluateReasons(App(failedChecks: 99), config);
        Assert.DoesNotContain(UpdateHealthReason.FailedChecks, reasons);
    }

    [Fact]
    public void Per_app_numeric_override_beats_global()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["updateHealthFailedChecks"] = JsonSerializer.SerializeToElement("20"),
        };
        var config = UpdateHealthService.ResolveConfig(settings, Defaults);
        var reasons = UpdateHealthService.EvaluateReasons(App(failedChecks: 10), config);
        Assert.DoesNotContain(UpdateHealthReason.FailedChecks, reasons);
    }

    [Fact]
    public void Stale_release_flags_old_releases()
    {
        var old = DateTimeOffset.Now.AddDays(-200);
        var config = UpdateHealthService.ResolveConfig(new Dictionary<string, JsonElement>(), Defaults);
        var reasons = UpdateHealthService.EvaluateReasons(App(releaseDateUs: old.ToUnixTimeMilliseconds() * 1000), config);
        Assert.Contains(UpdateHealthReason.StaleRelease, reasons);
    }

    [Fact]
    public void Archived_requires_flag_enabled()
    {
        var config = UpdateHealthService.ResolveConfig(new Dictionary<string, JsonElement>(), Defaults);
        Assert.Contains(UpdateHealthReason.Archived, UpdateHealthService.EvaluateReasons(App(archived: true), config));
        var settings = new Dictionary<string, JsonElement>
        {
            ["updateHealthArchived"] = JsonSerializer.SerializeToElement("off"),
        };
        config = UpdateHealthService.ResolveConfig(settings, Defaults);
        Assert.DoesNotContain(UpdateHealthReason.Archived, UpdateHealthService.EvaluateReasons(App(archived: true), config));
    }

    [Fact]
    public void Ignored_when_dismissed_for_current_version()
    {
        var app = App(failedChecks: 99);
        app.UpdateHealthDismissedVersion = app.LatestVersion;
        Assert.False(UpdateHealthService.HasIssues(app, Defaults));
    }

    [Fact]
    public void Disabled_apps_never_flag()
    {
        var app = App(failedChecks: 99);
        app.Disabled = true;
        Assert.False(UpdateHealthService.HasIssues(app, Defaults));
    }
}
