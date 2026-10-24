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
    public void Import_accepts_a_bare_apps_array()
    {
        var (imported, hasSettings, _) = AppStore.Import("""[{"id":"x","url":"https://github.com/a/b"}]""");
        Assert.False(hasSettings);
        Assert.Equal("x", Assert.Single(imported).Id);
    }
}
