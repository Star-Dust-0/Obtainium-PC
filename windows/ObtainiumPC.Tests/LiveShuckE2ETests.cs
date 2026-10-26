using Xunit;
using System.Diagnostics;
using ObtainiumPC.Services.Detection;
using ObtainiumPC.Services.Download;

namespace ObtainiumPC.Tests;

/// Live end-to-end proof on an isolated CI runner (never on a user machine):
/// downloads the real Shuck v1.0.1 installer through the real DownloadService from the
/// GitHub asset API URL, checks size and SHA-256, silently installs it (NSIS /S),
/// then runs the app's registry detection. Runs only when OBT_LIVE_E2E=1.
public class LiveShuckE2ETests
{
    private const string AssetApi = "https://api.github.com/repos/shuck-app/shuck/releases/assets/658648512";
    private const string Sha = "602c136f2fb95fe7a489fe550e6986860d0f092d59889caa765d67c4e636e971";

    [Fact]
    public async Task RealShuckDownloadInstallAndDetection()
    {
        if (Environment.GetEnvironmentVariable("OBT_LIVE_E2E") != "1") return;
        var path = await new DownloadService().DownloadAsync(AssetApi, "Shuck_1.0.1_x64-setup.exe");
        Assert.Equal(6297561, new FileInfo(path).Length);
        Assert.Equal(Sha, DownloadService.Sha256Of(path));
        Assert.True(DownloadService.IsValidPe(path));
        var chk = await DownloadService.VerifyAgainstReleaseChecksumAsync(path, "Shuck_1.0.1_x64-setup.exe",
            new[] { new KeyValuePair<string, string>("SHA256SUMS", "https://github.com/shuck-app/shuck/releases/download/v1.0.1/SHA256SUMS") });
        Console.WriteLine("checksum status: " + chk);
        var p = Process.Start(new ProcessStartInfo(path, "/S") { UseShellExecute = false })!;
        Assert.True(p.WaitForExit(120000), "silent installer did not finish");
        Console.WriteLine("installer exit code " + p.ExitCode);
        await Task.Delay(3000);
        var apps = InstalledAppsService.ListInstalledApps();
        foreach (var a in apps.Where(a => a.DisplayName.Contains("shuck", StringComparison.OrdinalIgnoreCase) || (a.KeyName ?? "").Contains("shuck", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"REGISTRY: name='{a.DisplayName}' version='{a.DisplayVersion}' key='{a.KeyName}' icon='{a.DisplayIcon}' loc='{a.InstallLocation}'");
        var m = InstalledAppsService.FindMatch("shuck", "shuck-app", apps);
        Assert.NotNull(m);
        Console.WriteLine($"MATCH: {m!.Info.DisplayName} {m.Info.DisplayVersion} confidence={m.Confidence}");
        Assert.StartsWith("1.0.1", m.Info.DisplayVersion ?? "");
    }
}
