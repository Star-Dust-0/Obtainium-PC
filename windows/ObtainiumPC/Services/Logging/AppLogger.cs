using System.Text.Json;
using ObtainiumPC.Services.Settings;

namespace ObtainiumPC.Services.Logging;

public enum AppLogLevel { Debug, Info, Warning, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, AppLogLevel Level, string Message, string? Error)
{
    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Level}: {Message}" + (Error != null ? $"\n{Error}" : "");
}

/// <summary>
/// Port of lib/core/logging/app_logger.dart over a JSONL file store with the
/// same 7-day retention. Logging must never break the app: every write is
/// individually guarded.
/// </summary>
public static class AppLogger
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppSettings.DataDir, "logs.jsonl");
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public static void Debug(string message, Exception? error = null) => Write(AppLogLevel.Debug, message, error);
    public static void Info(string message, Exception? error = null) => Write(AppLogLevel.Info, message, error);
    public static void Warn(string message, Exception? error = null) => Write(AppLogLevel.Warning, message, error);
    public static void Error(string message, Exception? error = null) => Write(AppLogLevel.Error, message, error);

    private static void Write(AppLogLevel level, string message, Exception? error)
    {
        try
        {
            var entry = new LogEntry(DateTimeOffset.Now, level, message, error?.ToString());
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry) + "\n");
            }
        }
        catch { /* logging never throws */ }
    }

    public static IReadOnlyList<LogEntry> GetLogs(DateTimeOffset? after = null)
    {
        var results = new List<LogEntry>();
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return results;
                foreach (var line in File.ReadLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<LogEntry>(line);
                        if (entry != null && (after == null || entry.Timestamp >= after)) results.Add(entry);
                    }
                    catch { /* skip malformed lines */ }
                }
            }
        }
        catch { /* unreadable store */ }
        return results;
    }

    public static void ClearLogs()
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
        }
        catch { /* ignore */ }
    }

    /// Called at startup; drops entries older than the retention window.
    public static void PurgeOld()
    {
        try
        {
            var cutoff = DateTimeOffset.Now - Retention;
            List<LogEntry> kept;
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return;
                kept = File.ReadLines(FilePath)
                    .Select(line => { try { return JsonSerializer.Deserialize<LogEntry>(line); } catch { return null; } })
                    .Where(e => e != null && e.Timestamp >= cutoff)
                    .Cast<LogEntry>()
                    .ToList();
                File.WriteAllLines(FilePath, kept.Select(e => JsonSerializer.Serialize(e)));
            }
        }
        catch { /* ignore */ }
    }
}
