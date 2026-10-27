namespace ObtainiumPC.Services.Download;

/// <summary>One in-flight download of a tracked app.</summary>
public sealed class DownloadEntry
{
    private long _received;
    private long? _total;

    public DownloadEntry(string appId, string fileName) { AppId = appId; FileName = fileName; }

    public string AppId { get; }
    public string FileName { get; }
    public (long Received, long? Total) Snapshot { get { lock (this) return (_received, _total); } }

    /// Raised on the thread that reports progress; subscribers marshal to the UI thread themselves.
    public event Action? Changed;
    public event Action? Ended;

    internal void Report(long received, long? total)
    {
        lock (this) { _received = received; _total = total; }
        Changed?.Invoke();
    }

    internal void RaiseEnded() => Ended?.Invoke();
}

/// <summary>
/// App-wide record of which apps are downloading right now. A page that is
/// opened again while a download runs reads it instead of offering a second
/// download of the same app (two writers on the same .part file), and can
/// show the live progress again.
/// </summary>
public static class DownloadRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DownloadEntry> Active = new();

    /// Registers a download for the app, or returns null when one is already running.
    public static DownloadEntry? TryBegin(string appId, string fileName)
    {
        lock (Gate)
        {
            if (Active.ContainsKey(appId)) return null;
            var entry = new DownloadEntry(appId, fileName);
            Active[appId] = entry;
            return entry;
        }
    }

    public static DownloadEntry? Get(string appId)
    {
        lock (Gate) return Active.TryGetValue(appId, out var e) ? e : null;
    }

    public static void Report(DownloadEntry? entry, long received, long? total) => entry?.Report(received, total);

    public static void End(DownloadEntry? entry)
    {
        if (entry == null) return;
        lock (Gate)
        {
            if (Active.TryGetValue(entry.AppId, out var cur) && ReferenceEquals(cur, entry)) Active.Remove(entry.AppId);
        }
        entry.RaiseEnded();
    }
}
