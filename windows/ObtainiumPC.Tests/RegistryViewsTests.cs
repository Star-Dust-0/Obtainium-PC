using Microsoft.Win32;
using ObtainiumPC.Services.Detection;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Writes throw-away uninstall entries (random names, removed afterwards) into the real 32-bit and
/// 64-bit HKLM views and checks the probe sees both. Fails (never silently passes) when HKLM is not writable; set OBT_REGISTRY_TESTS_OPTIONAL=1 to opt out locally.
/// </summary>
public class RegistryViewsTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public RegistryViewsTests(Xunit.Abstractions.ITestOutputHelper o) { _out = o; }

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
        Assert.True(OperatingSystem.IsWindows());
        var admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        _out.WriteLine("REGISTRY-VIEWS-TEST elevated=" + admin);
        var id = Guid.NewGuid().ToString("N");
        var n64 = "ObtTest64-" + id; var n32 = "ObtTest32-" + id;
        try
        {
            if (!TryWrite(RegistryView.Registry64, n64, "ObtTest64 App " + id))
            {
                Assert.True(Environment.GetEnvironmentVariable("OBT_REGISTRY_TESTS_OPTIONAL") == "1", "HKLM not writable (elevated=" + admin + "): registry view scenario NOT exercised");
                return;
            }
            Assert.True(TryWrite(RegistryView.Registry32, n32, "ObtTest32 App " + id));
            // 32-bit write must land in WOW6432Node of the 64-bit view.
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = root.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + n32))
                Assert.NotNull(k);
            var probe = InstalledAppsService.ProbeInstalledApps();
            Assert.True(probe.AllHivesRead);
            Assert.Contains(probe.Apps, a => a.DisplayName == "ObtTest64 App " + id);
            Assert.Contains(probe.Apps, a => a.DisplayName == "ObtTest32 App " + id);
            _out.WriteLine("REGISTRY-VIEWS-TEST both temporary entries (64-bit view and 32-bit view) were detected by the probe");
        }
        finally { Remove(RegistryView.Registry64, n64); Remove(RegistryView.Registry32, n32); }
    }
}
