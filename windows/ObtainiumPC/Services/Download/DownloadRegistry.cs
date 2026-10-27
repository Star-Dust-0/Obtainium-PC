namespace ObtainiumPC.Services.Download;

public enum DownloadState
{
    Downloading,
    /// File saved; not a Windows installer, so nothing was launched.
    Saved,
    /// Installer was launched; the installed version has not been verified (yet).
    InstallerOpened,
    Cancelled,
    Failed,
}

/// <summary>One download job of a tracked app, live or finished.</summary>
public sealed class DownloadEntry
{
    private long _received;
    private long? _total;
    private readonly CancellationTokenSource _cts = new();
    private long _lastRaiseTicks;

    public DownloadEntry(string appId, string fileName, string? appName = null)
    {
        AppId = appId; FileName = fileName; AppName = appName ?? appId;
    }

    public string AppId { get; }
    public string AppName { get; }
    public string FileName { get; }
    public DownloadState State { get; private set; } = DownloadState.Downloading;
    /// Short English outcome text for failed/cancelled/saved states.
    public string? Message { get; private set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; private set; }
    public CancellationToken Token => _cts.Token;
    public bool CancelRequested => _cts.IsCancellationRequested;
    public (long Received, long? Total) Snapshot { get { lock (this) return (_received, _total); } }

    /// Raised on the thread that reports progress; subscribers marshal to the UI thread themselves.
    public event Action? Changed;
    public event Action? Ended;

    /// Asks the running transfer to stop. The transfer deletes only its own .part file.
    public void Cancel()
    {
        if (State != DownloadState.Downloading) return;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        Changed?.Invoke();
    }

    internal void Report(long received, long? total)
    {
        lock (this) { _received = received; _total = total; }
        // Progress arrives per 80 KB read; the UI needs a few updates per second.
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRaiseTicks) < 150) return;
        Interlocked.Exchange(ref _lastRaiseTicks, now);
        Changed?.Invoke();
    }

    internal void Finish(DownloadState state, string? message)
    {
        State = state; Message = message; FinishedAt = DateTimeOffset.Now;
        Changed?.Invoke();
        Ended?.Invoke();
    }
}

/// <summary>
/// App-wide record of downloads. A page opened again while a download runs reads it
/// instead of offering a second download of the same app (two writers on the same
/// .part file), the global strip and Downloads list read it, and a finished job stays
/// listed (with an honest state) until the user clears it.
/// </summary>
public static class DownloadRegistry
{
    public const int HistoryLimit = 20;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DownloadEntry> Active = new();
    private static readonly List<DownloadEntry> History = new();

    /// Raised when any job starts, changes or ends (not marshalled to a UI thread).
    public static event Action? ListChanged;

    /// Registers a download for the app, or returns null when one is already running.
    public static DownloadEntry? TryBegin(string appId, string fileName, string? appName = null)
    {
        DownloadEntry entry;
        lock (Gate)
        {
            if (Active.ContainsKey(appId)) return null;
            entry = new DownloadEntry(appId, fileName, appName);
            Active[appId] = entry;
            // A new job for the app replaces its earlier finished row.
            History.RemoveAll(h => h.AppId == appId);
        }
        entry.Changed += RaiseListChanged;
        ListChanged?.Invoke();
        return entry;
    }

    private static void RaiseListChanged() => ListChanged?.Invoke();

    public static DownloadEntry? Get(string appId)
    {
        lock (Gate) return Active.TryGetValue(appId, out var e) ? e : null;
    }

    public static void Report(DownloadEntry? entry, long received, long? total) => entry?.Report(received, total);

    /// Ends the job with a final state; it moves to the finished list.
    public static void End(DownloadEntry? entry, DownloadState state = DownloadState.Saved, string? message = null)
    {
        if (entry == null) return;
        lock (Gate)
        {
            if (Active.TryGetValue(entry.AppId, out var cur) && ReferenceEquals(cur, entry))
            {
                Active.Remove(entry.AppId);
                History.Insert(0, entry);
                if (History.Count > HistoryLimit) History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
            }
        }
        entry.Finish(state, message);
        ListChanged?.Invoke();
    }

    public static IReadOnlyList<DownloadEntry> ActiveEntries()
    {
        lock (Gate) return Active.Values.OrderBy(e => e.StartedAt).ToList();
    }

    public static IReadOnlyList<DownloadEntry> FinishedEntries()
    {
        lock (Gate) return History.ToList();
    }

    public static int ActiveCount { get { lock (Gate) return Active.Count; } }

    public static void ClearFinished()
    {
        lock (Gate) History.Clear();
        ListChanged?.Invoke();
    }

    /// Cancels every running job (each deletes only its own partial file).
    public static void CancelAll()
    {
        foreach (var e in ActiveEntries()) e.Cancel();
    }

    /// Combined progress of the running jobs; Fraction is null while any total is unknown.
    public static (long Received, long? Total, double? Fraction) Aggregate()
    {
        long received = 0, total = 0; var known = true;
        foreach (var e in ActiveEntries())
        {
            var (r, t) = e.Snapshot;
            received += r;
            if (t is > 0) total += t.Value; else known = false;
        }
        return (received, known && total > 0 ? total : null, known && total > 0 ? Math.Min(1.0, (double)received / total) : null);
    }
}
