using ObtainiumPC.Models;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

public class ManualInstallLinkTests
{
    private static readonly List<InstalledAppInfo> Installed = new()
    {
        new("Zed Editor", "3.1", null, "zed", "ZedEditor"),
        new("Acme Paint (renamed)", "7.2.0", null, "acme", "AcmePaint_is1"),
        new("No Key Program", "1.0", null, null, null),
    };

    [Fact]
    public void Candidates_need_a_key_are_sorted_and_follow_the_filter()
    {
        var all = ManualInstallLink.Candidates(Installed, null);
        Assert.Equal(new[] { "Acme Paint (renamed)", "Zed Editor" }, all.Select(a => a.DisplayName).ToArray());
        Assert.Single(ManualInstallLink.Candidates(Installed, "  paint "));
        Assert.Empty(ManualInstallLink.Candidates(Installed, "nothing"));
    }

    [Fact]
    public void Linking_records_the_key_and_version_and_later_matching_uses_the_key_even_when_the_names_differ()
    {
        var app = new TrackedApp { Name = "Paint", Author = "somebody" };
        Assert.Null(InstalledAppsService.FindMatchFor(app, Installed));   // the name rules do not find it
        Assert.True(ManualInstallLink.Apply(app, Installed[1]));
        Assert.Equal("7.2.0", app.InstalledVersion);
        Assert.Equal("os", app.InstalledVersionSource);
        Assert.Equal("Acme Paint (renamed)", app.OsMatchedName);
        var m = InstalledAppsService.FindMatchFor(app, Installed);
        Assert.Equal(InstalledAppsService.MatchConfidence.Exact, m!.Confidence);
        Assert.Equal("AcmePaint_is1", m.Info.KeyName);
        // A later version of the same program is read through the key.
        var newer = new List<InstalledAppInfo> { new("Acme Paint 2", "8.0.0", null, "acme", "AcmePaint_is1") };
        Assert.Equal("8.0.0", InstalledAppsService.FindMatchFor(app, newer)!.Info.DisplayVersion);
    }

    [Fact]
    public void A_program_without_a_key_or_version_is_not_linked_and_nothing_changes()
    {
        var app = new TrackedApp { Name = "Paint", InstalledVersion = "1.0" };
        Assert.False(ManualInstallLink.Apply(app, Installed[2]));
        Assert.False(ManualInstallLink.Apply(app, new InstalledAppInfo("X", " ", null, null, "k")));
        Assert.Equal("1.0", app.InstalledVersion);
        Assert.Null(app.GetSettingString(InstalledAppsService.InstalledKeySetting));
    }

    [Fact]
    public void Clear_forgets_the_remembered_key()
    {
        var app = new TrackedApp { Name = "Paint" };
        ManualInstallLink.Apply(app, Installed[1]);
        ManualInstallLink.Clear(app);
        Assert.Null(app.GetSettingString(InstalledAppsService.InstalledKeySetting));
    }
}
