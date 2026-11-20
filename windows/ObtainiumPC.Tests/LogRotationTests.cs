using ObtainiumPC.Services.Logging;
using ObtainiumPC.Services.Settings;
using Xunit;

namespace ObtainiumPC.Tests;

[Collection("settings-dir")]
public class LogRotationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "obpc-log-" + Guid.NewGuid().ToString("N"));
    private readonly long _old = AppLogger.MaxBytes;
    public LogRotationTests() { Directory.CreateDirectory(_dir); AppSettings.DirOverride = _dir; }
    public void Dispose() { AppLogger.MaxBytes = _old; AppSettings.DirOverride = null; try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void A_log_past_the_cap_is_rotated_and_new_entries_start_a_fresh_file()
    {
        AppLogger.MaxBytes = 2000;
        for (int i = 0; i < 60; i++) AppLogger.Info("entry " + i + new string('x', 80));
        Assert.True(File.Exists(Path.Combine(_dir, "logs.previous.jsonl")));
        var current = new FileInfo(Path.Combine(_dir, "logs.jsonl"));
        Assert.True(current.Exists && current.Length <= 2000 + 400);
        Assert.Contains(AppLogger.GetLogs(), e => e.Message.StartsWith("entry 59"));
    }
}
