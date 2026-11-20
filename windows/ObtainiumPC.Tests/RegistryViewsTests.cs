using Microsoft.Win32;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Writes throw-away uninstall entries (random names, removed afterwards) into the real 32-bit and
/// 64-bit HKLM views and checks the probe sees both. Skips when HKLM is not writable (no admin).
/// </summary>
public class RegistryViewsTests
{
    private const string Path64 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static bool TryWrite(RegistryView view, string name, string display)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var k = root.CreateSubKey(Path64 + "\\" + name, true);
            k.SetValue("DisplayName", display);
            k.SetValue("DisplayVersion", "1.2.3");
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void Remove(RegistryView view, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            root.DeleteSubKeyTree(Path64 + "\\" + name, false);
        }
        catch (Exception) { }
    }

    [Fact]
    public void ProbeSeesBothThe64BitAndThe32BitUninstallViews()
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = Guid.NewGuid().ToString("N");
        var n64 = "ObtTest64-" + id; var n32 = "ObtTest32-" + id;
        try
        {
            if (!TryWrite(RegistryView.Registry64, n64, "ObtTest64 App " + id)) return; // not admin
            Assert.True(TryWrite(RegistryView.Registry32, n32, "ObtTest32 App " + id));
            // 32-bit write must land in WOW6432Node of the 64-bit view.
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = root.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + n32))
                Assert.NotNull(k);
            var probe = InstalledAppsService.ProbeInstalledApps();
            Assert.True(probe.AllHivesRead);
            Assert.Contains(probe.Apps, a => a.DisplayName == "ObtTest64 App " + id);
            Assert.Contains(probe.Apps, a => a.DisplayName == "ObtTest32 App " + id);
        }
        finally { Remove(RegistryView.Registry64, n64); Remove(RegistryView.Registry32, n32); }
    }
}
