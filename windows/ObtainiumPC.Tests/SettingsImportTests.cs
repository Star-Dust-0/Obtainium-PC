using System.Text.Json;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests;

public class SettingsImportTests
{
    [Fact]
    public void A_complete_export_restores_settings_and_tokens_on_a_fresh_install()
    {
        var src = new AppSettings { Language = "ar", ParallelDownloads = false, GitHubPersonalAccessToken = "ghp_roundtrip_synthetic", AutoExportPath = @"C:\other-machine\x.json" };
        var el = JsonSerializer.SerializeToElement(src);
        var dict = el.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        var json = AppStore.Export(new List<ObtainiumPC.Models.TrackedApp>(), dict, 2, "t", complete: true);
        var (_, hasSettings, imported) = AppStore.Import(json);
        Assert.True(hasSettings);
        var fresh = new AppSettings();
        var n = fresh.ApplyImported(imported!);
        Assert.True(n > 3);
        Assert.Equal("ar", fresh.Language);
        Assert.False(fresh.ParallelDownloads);
        Assert.Equal("ghp_roundtrip_synthetic", fresh.GitHubPersonalAccessToken);
        Assert.Null(fresh.AutoExportPath);   // machine-local, not carried over
    }

    [Fact]
    public void Unknown_keys_and_wrong_types_are_skipped_not_fatal()
    {
        var s = new AppSettings { Language = "en" };
        var vals = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"Language\":123,\"NoSuchSetting\":1,\"ParallelDownloads\":false}")!;
        var n = s.ApplyImported(vals);
        Assert.Equal(1, n);
        Assert.Equal("en", s.Language);
        Assert.False(s.ParallelDownloads);
    }
}
