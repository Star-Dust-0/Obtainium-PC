using ObtainiumPC.Services.Settings;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// Export/import roundtrip against the Obtainium schema-v2 format. Expectations
/// from upstream lib/providers/apps_provider.dart export/import: credential
/// keys ('*-creds') are stripped unless secrets are explicitly included, and
/// per-app asset lists/settings survive as JSON-encoded strings.
/// </summary>
public class ImportExportTests
{
    private static TrackedApp SampleApp()
    {
        var app = new TrackedApp
        {
            Id = "https://github.com/owner/app",
            Url = "https://github.com/owner/app",
            Author = "owner",
            Name = "app",
            LatestVersion = "1.2.3",
            InstalledVersion = "1.2.0",
            Pinned = true,
            Categories = new List<string> { "tools" },
        };
        app.AssetUrls = new List<KeyValuePair<string, string>>
        {
            new("app-1.2.3-win-x64.exe", "https://example.com/app.exe"),
        };
        app.SetSetting("trackOnly", false);
        app.SetSetting("github-creds", "user:secret");
        return app;
    }

    [Fact]
    public void Export_import_roundtrip_preserves_app_fields()
    {
        var json = AppStore.Export(new[] { SampleApp() }, null, 0, "1.0.0-test");
        var (imported, hasSettings, _) = AppStore.Import(json);

        Assert.False(hasSettings);
        var app = Assert.Single(imported);
        Assert.Equal("https://github.com/owner/app", app.Id);
        Assert.Equal("owner", app.Author);
        Assert.Equal("app", app.Name);
        Assert.Equal("1.2.3", app.LatestVersion);
        Assert.Equal("1.2.0", app.InstalledVersion);
        Assert.True(app.Pinned);
        Assert.Equal(new List<string> { "tools" }, app.Categories);
        var asset = Assert.Single(app.AssetUrls);
        Assert.Equal("app-1.2.3-win-x64.exe", asset.Key);
        Assert.Equal("https://example.com/app.exe", asset.Value);
    }

    [Fact]
    public void Exporting_without_credentials_does_not_remove_them_from_the_live_app()
    {
        var app = SampleApp();
        AppStore.Export(new[] { app }, null, 0, "1.0.0-test");
        AppStore.Export(new[] { app }, null, 1, "1.0.0-test");
        Assert.Equal("user:secret", app.GetSettingString("github-creds"));
    }

    [Fact]
    public void SamePath_matches_case_and_relative_forms_and_rejects_empty()
    {
        var d = Path.GetTempPath();
        Assert.True(AppStore.SamePath(Path.Combine(d, "Backup.json"), Path.Combine(d, ".", "backup.JSON")));
        Assert.False(AppStore.SamePath(Path.Combine(d, "a.json"), Path.Combine(d, "b.json")));
        Assert.False(AppStore.SamePath(null, Path.Combine(d, "a.json")));
        Assert.False(AppStore.SamePath("", ""));
    }

    [Fact]
    public void Complete_export_includes_credentials_at_every_level_and_roundtrips_synthetic_secrets()
    {
        var settings = new Dictionary<string, object?>
        {
            ["GitHubPersonalAccessToken"] = "synthetic-gh-token",
            ["GitLabPersonalAccessToken"] = "synthetic-gl-token",
            ["Theme"] = 2,
        };
        foreach (var level in new[] { 0, 1, 2 })
        {
            var json = AppStore.Export(new[] { SampleApp() }, settings, level, "1.0.0-test", complete: true);
            Assert.Contains("synthetic-gh-token", json);
            Assert.Contains("synthetic-gl-token", json);
            var (apps, hasSettings, imported) = AppStore.Import(json);
            Assert.True(hasSettings);
            Assert.Equal("user:secret", apps[0].GetSettingString("github-creds"));
            Assert.Equal("synthetic-gh-token", imported!["GitHubPersonalAccessToken"].GetString());
        }
    }

    [Fact]
    public void Default_export_strips_all_credentials_and_opt_in_includes_them()
    {
        var settings = new Dictionary<string, object?>
        {
            ["GitHubPersonalAccessToken"] = "synthetic-gh-token",
            ["GitLabPersonalAccessToken"] = "synthetic-gl-token",
            ["ForgejoPersonalAccessToken"] = "synthetic-fj-token",
            ["Theme"] = 2,
        };
        foreach (var level in new[] { 0, 1 })
        {
            var json = AppStore.Export(new[] { SampleApp() }, settings, level, "t");
            Assert.DoesNotContain("synthetic-", json);
            Assert.DoesNotContain("user:secret", json);
        }
        var opt = AppStore.Export(new[] { SampleApp() }, settings, 1, "t", complete: true);
        Assert.Contains("synthetic-gh-token", opt);
        Assert.Contains("synthetic-fj-token", opt);
        Assert.Contains("user:secret", opt);
    }

    [Fact]
    public void Default_export_omits_request_headers_and_opt_in_keeps_them_without_touching_the_live_app()
    {
        var app = SampleApp();
        var s = app.AdditionalSettings;
        s["requestHeader"] = System.Text.Json.JsonSerializer.SerializeToElement(new[] { new Dictionary<string, string> { ["requestHeader"] = "Authorization: Bearer synthetic-header-authz" } });
        app.AdditionalSettings = s;
        foreach (var level in new[] { 0, 1 })
            Assert.DoesNotContain("synthetic-header-authz", AppStore.Export(new[] { app }, null, level, "t"));
        Assert.Contains("synthetic-header-authz", AppStore.Export(new[] { app }, null, 1, "t", complete: true));
        Assert.Contains("synthetic-header-authz", app.AdditionalSettingsJson);
    }

    [Fact]
    public void Include_credentials_opt_in_defaults_off_and_is_not_imported()
    {
        Assert.False(new AppSettings().ExportIncludeCredentials);
        var fresh = new AppSettings();
        fresh.ApplyImported(new Dictionary<string, System.Text.Json.JsonElement> { ["ExportIncludeCredentials"] = System.Text.Json.JsonSerializer.SerializeToElement(true) });
        Assert.False(fresh.ExportIncludeCredentials);
    }

    [Fact]
    public void Export_level_setting_defaults_to_stripped_and_clamps_like_upstream()
    {
        // lib/providers/settings_provider.dart exportSettings: default 1
        // (apps + settings, credentials stripped), out-of-range resets to 1.
        // Auto-export uses this level, so a default auto-export can never
        // contain credentials; level 2 requires an explicit selection.
        var s = new Services.Settings.AppSettings();
        Assert.Equal(1, s.ExportSettingsLevel);
        s.ExportSettingsLevel = 2; Assert.Equal(2, s.ExportSettingsLevel);
        s.ExportSettingsLevel = 9; Assert.Equal(1, s.ExportSettingsLevel);
        s.ExportSettingsLevel = -1; Assert.Equal(1, s.ExportSettingsLevel);
        s.ExportSettingsLevel = 0; Assert.Equal(0, s.ExportSettingsLevel);
    }

    [Fact]
    public void Import_accepts_a_bare_apps_array()
    {
        var (imported, hasSettings, _) = AppStore.Import("""[{"id":"x","url":"https://github.com/a/b"}]""");
        Assert.False(hasSettings);
        Assert.Equal("x", Assert.Single(imported).Id);
    }
}
