namespace ObtainiumPC.Services.Download;

public enum InstallCheckResult
{
    /// The installed version equals the downloaded release.
    Confirmed,
    /// The app is found and still reports the version it had before the download.
    Unchanged,
    /// The app is found but reports some other version (neither the old nor the new one).
    Different,
    /// The installed version cannot be read at all.
    NotDetected,
}

/// <summary>Independent installed-version check after an installer was launched.
/// An installer's exit is never proof of success; only the version it left behind is.</summary>
public static class InstallCheck
{
    public static string Normalize(string? v)
    {
        var t = (v ?? "").Trim();
        if (t.Length > 1 && (t[0] == 'v' || t[0] == 'V') && char.IsDigit(t[1])) t = t.Substring(1);
        return t.ToLowerInvariant();
    }

    public static InstallCheckResult Evaluate(string? detectedVersion, string? expectedLatest, string? previousInstalled)
    {
        if (string.IsNullOrWhiteSpace(detectedVersion)) return InstallCheckResult.NotDetected;
        var d = Normalize(detectedVersion);
        if (!string.IsNullOrEmpty(expectedLatest) && (d == Normalize(expectedLatest) || Updates.SelfUpdate.SameRelease(detectedVersion, expectedLatest))) return InstallCheckResult.Confirmed;
        if (!string.IsNullOrEmpty(previousInstalled) && (d == Normalize(previousInstalled) || Updates.SelfUpdate.SameRelease(detectedVersion, previousInstalled))) return InstallCheckResult.Unchanged;
        return InstallCheckResult.Different;
    }

    /// Final wording once the installer has ended (or the wait ran out) without a confirmed version.
    public static string UnconfirmedMessage(InstallCheckResult last, string? detected, int? installerExitCode, bool installerEnded)
    {
        var exit = installerExitCode is { } c && c != 0 ? $" (installer exit code {c})" : "";
        return last switch
        {
            InstallCheckResult.Unchanged => (installerEnded ? "Installer ended" + exit + ", but the installed version is still " : "Still waiting: the installed version is still ") + detected + " - the update was not installed or was cancelled",
            InstallCheckResult.Different => "Installer ended" + exit + ", but the installed version is " + detected + ", not the downloaded release",
            _ => (installerEnded ? "Installer ended" + exit + ", but" : "Could not confirm:") + " the installed version could not be read automatically",
        };
    }
}
