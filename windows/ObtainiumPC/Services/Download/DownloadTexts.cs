namespace ObtainiumPC.Services.Download;

/// <summary>Plain English status text for the download strip and list. No UI types, unit-testable.</summary>
public static class DownloadTexts
{
    public static string Bytes(long n) => n >= 1048576 ? $"{n / 1048576.0:F1} MB" : $"{Math.Max(1, n / 1024)} KB";

    /// "14.8 MB of 60.1 MB" when the total is known, otherwise "14.8 MB so far".
    public static string Progress(long received, long? total) =>
        total is > 0 ? $"{received / 1048576.0:F1} MB of {total.Value / 1048576.0:F1} MB" : $"{Bytes(received)} so far";

    public static string Status(DownloadEntry e)
    {
        var (r, t) = e.Snapshot;
        return e.State switch
        {
            DownloadState.Downloading => e.CancelRequested ? "Cancelling..." : Progress(r, t),
            DownloadState.Saved => "Saved - not a Windows installer, so it was not launched",
            DownloadState.InstallerOpened => "Installer opened - the installed version has not been confirmed yet",
            DownloadState.Cancelled => "Cancelled - partial file removed",
            DownloadState.Failed => string.IsNullOrWhiteSpace(e.Message) ? "Failed" : "Failed - " + e.Message,
            _ => "",
        };
    }

    /// Two lines for the strip: title and detail. Active jobs win over finished ones.
    public static (string Title, string Detail) Strip(IReadOnlyList<DownloadEntry> active, IReadOnlyList<DownloadEntry> finished,
        long aggregateReceived, long? aggregateTotal)
    {
        if (active.Count == 1) return ("Downloading " + active[0].AppName, Status(active[0]));
        if (active.Count > 1) return ($"Downloading {active.Count} apps", Progress(aggregateReceived, aggregateTotal));
        if (finished.Count > 0) return (finished[0].AppName, Status(finished[0]));
        return ("", "");
    }
}
