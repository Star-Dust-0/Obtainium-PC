using ObtainiumPC.Services.Settings;
using Xunit;

namespace ObtainiumPC.Tests;

[Collection("settings-dir")]
public class SettingsStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obpc-settings-" + Guid.NewGuid().ToString("N"));
    public SettingsStorageTests() { Directory.CreateDirectory(_dir); AppSettings.DirOverride = _dir; }
    public void Dispose() { AppSettings.DirOverride = null; try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Corrupt_settings_are_kept_flagged_and_never_overwritten()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"GitHubPersonalAccessToken\": \"secret\", broken");
        var s = AppSettings.Load();
        Assert.True(AppSettings.LoadFailed);
        Assert.NotNull(AppSettings.LoadBackupPath);
        Assert.True(File.Exists(AppSettings.LoadBackupPath));
        s.Language = "ar";
        s.Save();   // blocked
        Assert.Contains("secret", File.ReadAllText(path));
        Assert.Contains("secret", File.ReadAllText(AppSettings.LoadBackupPath!));
    }

    [Fact]
    public void Valid_settings_round_trip_and_leave_no_temp_file()
    {
        var s = AppSettings.Load();
        Assert.False(AppSettings.LoadFailed);
        s.Language = "ar";
        s.Save();
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json.tmp")));
        Assert.Equal("ar", AppSettings.Load().Language);
    }
}
