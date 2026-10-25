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
    public void Credentials_are_stripped_unless_secrets_included()
    {
        var stripped = AppStore.Export(new[] { SampleApp() }, null, 0, "1.0.0-test");
        var (noCreds, _, _) = AppStore.Import(stripped);
        Assert.Null(noCreds[0].GetSettingString("github-creds"));
        Assert.True(noCreds[0].AdditionalSettings.ContainsKey("trackOnly"));

        var withSecrets = AppStore.Export(new[] { SampleApp() }, null, 2, "1.0.0-test");
        var (withCreds, _, _) = AppStore.Import(withSecrets);
        Assert.Equal("user:secret", withCreds[0].GetSettingString("github-creds"));
    }

    [Fact]
    public void Settings_level_secrets_are_stripped_unless_secrets_included()
    {
        // The port stores source PATs as typed *PersonalAccessToken settings;
        // upstream's "-creds" suffix strip must cover them so GitLab/Forgejo
        // PATs never leak into a "credentials stripped" export.
        var settings = new Dictionary<string, object?>
        {
            ["GitHubPersonalAccessToken"] = "gh-secret",
            ["GitLabPersonalAccessToken"] = "gl-secret",
            ["ForgejoPersonalAccessToken"] = "fj-secret",
            ["Theme"] = 2,
        };
        var stripped = AppStore.Export(new[] { SampleApp() }, settings, 1, "1.0.0-test");
        Assert.DoesNotContain("gh-secret", stripped);
        Assert.DoesNotContain("gl-secret", stripped);
        Assert.DoesNotContain("fj-secret", stripped);
        Assert.Contains("\"Theme\"", stripped);
        // Per-app source-option secrets must be absent from the raw emitted
        // document too - not only after a re-import (SampleApp carries
        // github-creds = user:secret in its additionalSettings).
        Assert.DoesNotContain("user:secret", stripped);
        Assert.DoesNotContain("github-creds", stripped);

        var withSecrets = AppStore.Export(new[] { SampleApp() }, settings, 2, "1.0.0-test");
        Assert.Contains("gh-secret", withSecrets);
        Assert.Contains("gl-secret", withSecrets);
        Assert.Contains("fj-secret", withSecrets);
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
