using ObtainiumPC.Models;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

/// ExternalUninstallService (lib/providers/apps_provider_lifecycle.dart
/// reconcileInstallStatus): clearing recorded installs on verifiable
/// external uninstall, with the Windows safety rules (exact recorded
/// identity, complete probe, os-provenance only).
public class ExternalUninstallServiceTests
{
    private static TrackedApp App(string id, string? source, string? matched) => new()
    {
        Id = id,
        Name = id,
        Url = "https://github.com/x/" + id,
        InstalledVersion = "1.0",
        InstalledVersionSource = source,
        OsMatchedName = matched,
    };

    private static readonly List<string> Installed = new() { "Other App", "Notepad++" };

    [Fact]
    public void Os_confirmed_install_verifiably_gone_is_cleared()
    {
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", "os", "Gone App") }, Installed, _ => true);
        Assert.Equal(new[] { "a" }, removed);
    }

    [Fact]
    public void Recorded_identity_still_present_is_kept()
    {
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", "os", "Notepad++") }, Installed, _ => true);
        Assert.Empty(removed);
    }

    [Fact]
    public void Identity_matching_uses_the_same_normalization_as_detection()
    {
        // "Gone-App 2024" recorded; "goneapp2024" present with different
        // punctuation/casing still counts as present.
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", "os", "Gone App") }, new List<string> { "goneapp" }, _ => true);
        Assert.Empty(removed);
    }

    [Fact]
    public void Manual_marks_are_never_cleared()
    {
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", "manual", null), App("b", "manual", "Gone App") }, Installed, _ => true);
        Assert.Empty(removed);
    }

    [Fact]
    public void Legacy_records_without_provenance_are_never_cleared()
    {
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", null, null) }, Installed, _ => true);
        Assert.Empty(removed);
    }

    [Fact]
    public void Detection_gated_apps_are_preserved()
    {
        // track-only / versionDetection off / source-disallowed: the caller's
        // predicate says no, so the app is preserved even when verifiably gone.
        var removed = ExternalUninstallService.FindExternallyRemoved(
            new[] { App("a", "os", "Gone App") }, Installed, _ => false);
        Assert.Empty(removed);
    }

    [Fact]
    public void Apps_without_recorded_install_are_skipped()
    {
        var app = App("a", "os", "Gone App");
        app.InstalledVersion = null;
        var removed = ExternalUninstallService.FindExternallyRemoved(new[] { app }, Installed, _ => true);
        Assert.Empty(removed);
    }
}
