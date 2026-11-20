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
}
