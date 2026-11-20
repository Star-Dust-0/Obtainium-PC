using System.IO.Compression;

namespace ObtainiumPC.Services.Download;

/// <param name="Folder">The folder the app was extracted to.</param>
/// <param name="FileCount">Number of files written.</param>
/// <param name="UsedFallbackRoot">True when the default root could not be created and the per-user fallback was used.</param>
public sealed record ExtractionResult(string Folder, int FileCount, bool UsedFallbackRoot);

/// <summary>
/// Extracts a downloaded .zip (a portable app) to "&lt;system drive&gt;\Obtainium\&lt;zip name&gt;\".
/// Pure file logic with no UI types, so it is unit-testable.
///
/// Behaviour:
/// - The target folder is named after the zip file (without ".zip").
/// - A zip whose entries all sit under one top-level folder is flattened, so the program
///   ends up directly in the target folder instead of one level deeper.
/// - Everything is extracted to a staging folder first (zip-slip checked, disk space checked)
///   and only then moved into the target, so a corrupt zip never leaves a half-written app.
/// - Re-extracting into an existing folder overwrites same-named files and keeps the rest
///   (settings and data a portable app wrote next to itself survive).
/// </summary>
public static class ZipExtractionService
{
    public const string RootFolderName = "Obtainium";

    /// Upper bounds against zip bombs: 8 GiB written and 50,000 files.
    public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxFileCount = 50_000;

    /// A path segment Windows cannot store as written: reserved device names (with or without an
    /// extension), a trailing dot or space, or ".." / ".".
    public static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment == "." || segment == "..") return false;
        if (segment.EndsWith('.') || segment.EndsWith(' ')) return false;
        var stem = segment;
        var dot = stem.IndexOf('.');
        if (dot > 0) stem = stem.Substring(0, dot);
        stem = stem.TrimEnd(' ');
        return !Array.Exists(ReservedNames, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// "C:\Obtainium" on a normal install (the system drive's root).
    public static string DefaultRoot()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        return Path.Combine(string.IsNullOrEmpty(systemRoot) ? "C:\\" : systemRoot, RootFolderName);
    }

    /// Used only when the default root cannot be created (locked-down machine).
    public static string FallbackRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RootFolderName);

    /// The folder name for a zip: its file name without the extension, made safe for Windows.
    public static string FolderNameFor(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath) ?? "";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "app";
        if (name.Length > 100) name = name.Substring(0, 100).TrimEnd('.', ' ');
        if (Array.Exists(ReservedNames, r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase))) name = "_" + name;
        return name;
    }

    /// The single top-level folder shared by every entry ("app-1.0/"), or "" when there is none.
    public static string CommonRootFolder(IEnumerable<string> entryNames)
    {
        string? root = null;
        foreach (var raw in entryNames)
        {
            var name = raw.Replace('\\', '/').TrimStart('/');
            var slash = name.IndexOf('/');
            if (slash <= 0) return ""; // a file at the top level: nothing to flatten
            var first = name.Substring(0, slash);
            if (root == null) root = first;
            else if (!string.Equals(root, first, StringComparison.OrdinalIgnoreCase)) return "";
        }
        return root == null ? "" : root + "/";
    }

    public static Task<ExtractionResult> ExtractAsync(string zipPath, string? rootOverride = null, CancellationToken ct = default)
        => Task.Run(() => Extract(zipPath, rootOverride, ct), ct);

    public static ExtractionResult Extract(string zipPath, string? rootOverride = null, CancellationToken ct = default)
    {
        var root = rootOverride ?? DefaultRoot();
        var usedFallback = false;
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (rootOverride == null
            && (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException))
        {
            root = FallbackRoot();
            Directory.CreateDirectory(root);
            usedFallback = true;
        }

        var target = Path.Combine(root, FolderNameFor(zipPath));
        var staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            // Skip directory entries and macOS resource-fork clutter.
            var files = archive.Entries
                .Where(e => e.Name.Length > 0
                    && !e.FullName.Replace('\\', '/').StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (files.Count == 0) throw new InvalidDataException("The zip file is empty.");

            if (files.Count > MaxFileCount)
                throw new InvalidDataException("The zip contains too many files (" + files.Count + ").");
            var declared = files.Sum(f => f.Length);
            if (declared > MaxTotalBytes)
                throw new InvalidDataException("The zip is too large when extracted (" + DownloadTexts.Bytes(declared) + ").");
            EnsureFreeSpace(root, declared);

            var prefix = CommonRootFolder(files.Select(f => f.FullName));
            Directory.CreateDirectory(staging);
            var stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            var written = 0;
            long actual = 0;
            foreach (var entry in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (prefix.Length > 0) rel = rel.Substring(prefix.Length);
                if (rel.Length == 0) continue;
                var dest = Path.GetFullPath(Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar)));
                // Zip-slip: an entry such as "..\..\x" or "C:\x" must never leave the staging folder.
                if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The zip contains an unsafe path: " + entry.FullName);
                foreach (var seg in rel.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    if (!IsSafeSegment(seg))
                        throw new InvalidDataException("The zip contains an unsafe name: " + entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                // Copy by hand and count the real bytes: the sizes declared in a zip header can lie.
                using (var src = entry.Open())
                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        actual += n;
                        if (actual > MaxTotalBytes)
                            throw new InvalidDataException("The zip expands beyond the allowed size.");
                        dst.Write(buf, 0, n);
                    }
                }
                written++;
            }

            Directory.CreateDirectory(target);
            MergeInto(staging, target, ct);
            return new ExtractionResult(target, written, usedFallback);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* leftover staging folder is harmless */ }
        }
    }

    private static void MergeInto(string staging, string target, CancellationToken ct)
    {
        var stagingFull = Path.GetFullPath(staging);
        // Pre-flight: if an existing file is locked (the app is running), fail before moving anything,
        // so the folder is never left half old and half new.
        foreach (var src in Directory.EnumerateFiles(stagingFull, "*", SearchOption.AllDirectories))
        {
            var existing = Path.Combine(target, Path.GetRelativePath(stagingFull, src));
            if (!File.Exists(existing)) continue;
            try { using var fs = new FileStream(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Could not replace \"" + Path.GetRelativePath(stagingFull, src)
                    + "\". If the app is running, close it and try again.", ex);
            }
        }
        foreach (var src in Directory.EnumerateFiles(stagingFull, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(stagingFull, src);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                File.Move(src, dest, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new IOException("Could not replace \"" + rel + "\". If the app is running, close it and try again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException("Access denied while writing \"" + rel + "\". If the app is running, close it and try again.", ex);
            }
        }
    }

    private static void EnsureFreeSpace(string root, long neededBytes)
    {
        long free;
        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrEmpty(driveRoot)) return;
            free = new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch { return; } // cannot tell (network path, ...): let the extraction try
        // 10 % headroom for the staging copy being moved, not duplicated, on the same volume.
        if (neededBytes + neededBytes / 10 > free)
            throw new IOException("Not enough free disk space to extract this zip ("
                + DownloadTexts.Bytes(neededBytes) + " needed).");
    }
}
