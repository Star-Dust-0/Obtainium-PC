using ObtainiumPC.Services.Settings;
using Xunit;

namespace ObtainiumPC.Tests;

public class SettingsSecretStorageTests
{
    [Fact]
    public void Disk_copy_holds_no_clear_token_but_the_loaded_settings_and_export_do()
    {
        var s = new AppSettings { GitHubPersonalAccessToken = "ghp_synthetic_one", GitLabPersonalAccessToken = "glpat-synthetic-two", ForgejoPersonalAccessToken = "synthetic-three" };
        var disk = s.ToDisk();
        Assert.DoesNotContain("synthetic", disk);
        Assert.Contains("dpapi1:", disk);
        var back = AppSettings.FromDisk(disk);
        Assert.Equal("ghp_synthetic_one", back.GitHubPersonalAccessToken);
        Assert.Equal("glpat-synthetic-two", back.GitLabPersonalAccessToken);
        Assert.Equal("synthetic-three", back.ForgejoPersonalAccessToken);
        // The in-memory settings (what an export serializes) are untouched: complete, restorable exports.
        Assert.Equal("ghp_synthetic_one", s.GitHubPersonalAccessToken);
        Assert.Contains("ghp_synthetic_one", System.Text.Json.JsonSerializer.Serialize(s));
    }

    [Fact]
    public void A_plain_token_from_an_older_version_still_loads()
    {
        var back = AppSettings.FromDisk("{\"GitHubPersonalAccessToken\":\"ghp_legacy_plain\"}");
        Assert.Equal("ghp_legacy_plain", back.GitHubPersonalAccessToken);
    }

    [Fact]
    public void A_token_that_cannot_be_decrypted_reads_as_empty_not_as_garbage()
    {
        var back = AppSettings.FromDisk("{\"GitHubPersonalAccessToken\":\"dpapi1:AAAA\"}");
        Assert.Null(back.GitHubPersonalAccessToken);
    }

    [Fact]
    public void Empty_tokens_stay_empty()
    {
        Assert.DoesNotContain("dpapi1:", new AppSettings().ToDisk());
    }

    [Fact]
    public void An_undecryptable_settings_token_is_kept_on_disk_not_overwritten_by_empty()
    {
        var back = AppSettings.FromDisk("{\"GitHubPersonalAccessToken\":\"dpapi1:AAAA\"}");
        Assert.Null(back.GitHubPersonalAccessToken);
        Assert.Contains("dpapi1:AAAA", back.ToDisk());
        back.GitHubPersonalAccessToken = "ghp_new_synthetic";   // the user typing a new token replaces it
        var disk = back.ToDisk();
        Assert.DoesNotContain("dpapi1:AAAA", disk);
        Assert.Equal("ghp_new_synthetic", AppSettings.FromDisk(disk).GitHubPersonalAccessToken);
    }

    private static ObtainiumPC.Models.TrackedApp App(string addJson) =>
        new() { Id = "a1", Name = "x", Url = "https://github.com/o/x", AdditionalSettingsJson = addJson };

    [Fact]
    public void Per_app_credentials_are_encrypted_on_disk_and_clear_after_load_and_in_export()
    {
        var app = App("{\"github-creds\":\"ghp_app_synthetic\",\"trackOnly\":false}");
        var disk = ObtainiumPC.Services.Store.AppStore.SerializeForDisk(new List<ObtainiumPC.Models.TrackedApp> { app });
        Assert.DoesNotContain("ghp_app_synthetic", disk);
        Assert.Contains("dpapi1:", disk);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<List<ObtainiumPC.Models.TrackedApp>>(disk)!;
        ObtainiumPC.Services.Store.AppStore.OpenCredentials(loaded[0]);
        Assert.Equal("ghp_app_synthetic", loaded[0].GetSettingString("github-creds"));
        Assert.Contains("ghp_app_synthetic", ObtainiumPC.Services.Store.AppStore.Export(loaded, null, 0, "t", complete: true));
        Assert.Contains("ghp_app_synthetic", System.Text.Json.JsonSerializer.Serialize(app));
    }

    [Fact]
    public void An_undecryptable_per_app_credential_survives_the_next_save()
    {
        var app = App("{\"github-creds\":\"dpapi1:AAAA\",\"trackOnly\":false}");
        ObtainiumPC.Services.Store.AppStore.OpenCredentials(app);
        Assert.Null(app.GetSettingString("github-creds"));
        var disk = ObtainiumPC.Services.Store.AppStore.SerializeForDisk(new List<ObtainiumPC.Models.TrackedApp> { app });
        Assert.Contains("dpapi1:AAAA", disk);
    }
}
