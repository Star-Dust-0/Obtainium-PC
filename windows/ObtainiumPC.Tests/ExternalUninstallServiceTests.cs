using ObtainiumPC.Models;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

/// ExternalUninstallService (lib/providers/apps_provider_lifecycle.dart
/// reconcileInstallStatus): clearing recorded installs on verifiable
/// external uninstall. Absence of the recorded identity is an inference,
/// not proof - renamed/updated installs re-adopt their new identity first.
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

    private static List<ExternalUninstallService.ReconcileResult> Run(
        IEnumerable<TrackedApp> apps, List<string> installed,
        Func<TrackedApp, bool> gate, Func<TrackedApp, string?> match) =>
        ExternalUninstallService.Reconcile(apps, installed, gate, match);

    [Fact]
    public void Os_confirmed_install_verifiably_gone_is_cleared()
    {
        var r = Run(new[] { App("a", "os", "Gone App") }, Installed, _ => true, _ => null);
        Assert.Single(r);
        Assert.Equal(ExternalUninstallService.ReconcileAction.Clear, r[0].Action);
    }

    [Fact]
    public void Recorded_identity_still_present_is_untouched()
    {
        Assert.Empty(Run(new[] { App("a", "os", "Notepad++") }, Installed, _ => true, _ => null));
    }

    [Fact]
    public void Renamed_install_identity_is_readopted_not_cleared()
    {
        // "Foo 1.2" was recorded; the user upgraded and the DisplayName is now
        // "Foo 1.3". Old identity absent, but the matcher still finds the app.
        var r = Run(new[] { App("a", "os", "Foo 1.2") }, new List<string> { "Foo 1.3" },
            _ => true, _ => "Foo 1.3");
        Assert.Single(r);
        Assert.Equal(ExternalUninstallService.ReconcileAction.Readopt, r[0].Action);
        Assert.Equal("Foo 1.3", r[0].NewMatchedName);
    }

    [Fact]
    public void Manual_marks_are_never_touched()
    {
        Assert.Empty(Run(new[] { App("a", "manual", null), App("b", "manual", "Gone App") },
            Installed, _ => true, _ => null));
    }

    [Fact]
    public void Legacy_records_without_provenance_are_never_touched()
    {
        Assert.Empty(Run(new[] { App("a", null, null) }, Installed, _ => true, _ => null));
    }

    [Fact]
    public void Detection_gated_apps_are_preserved()
    {
        Assert.Empty(Run(new[] { App("a", "os", "Gone App") }, Installed, _ => false, _ => null));
    }

    [Fact]
    public void Apps_without_recorded_install_are_skipped()
    {
        var app = App("a", "os", "Gone App");
        app.InstalledVersion = null;
        Assert.Empty(Run(new[] { app }, Installed, _ => true, _ => null));
    }

    [Fact]
    public void Identity_matching_uses_detection_normalization()
    {
        Assert.Empty(Run(new[] { App("a", "os", "Gone App") }, new List<string> { "goneapp" }, _ => true, _ => null));
    }
}
