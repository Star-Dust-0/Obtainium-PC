using System.IO.Compression;

namespace ObtainiumPC.Services.Download;

/// <param name="Folder">The folder the app was extracted to.</param>
/// <param name="FileCount">Number of files written.</param>
/// <param name="UsedFallbackRoot">True when the default root could not be created and the per-user fallback was used.</param>
public sealed record ExtractionResult(string Folder, int FileCount, bool UsedFallbackRoot, IReadOnlyList<string>? KeptUserFiles = null);

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

    public static Task<ExtractionResult> ExtractAsync(string zipPath, string? rootOverride = null, CancellationToken ct = default, string? ownerId = null, string? folderName = null)
        => Task.Run(() => Extract(zipPath, rootOverride, ct, ownerId, folderName), ct);

    public const string OwnerMarkerFile = ".obtainium-owner";

    public const string ManifestFile = ".obtainium-manifest.json";

    /// One stable folder per app under the root (new versions replace the old in place). A folder of
    /// the same name is reused only when its owner marker matches this app; a folder from another app,
    /// or one Obtainium-PC did not create, is never reused: " (2)", " (3)" ... is appended.
    internal static string ChooseTarget(string root, string zipPath, string? ownerId, string? folderName = null)
    {
        var baseName = !string.IsNullOrWhiteSpace(folderName) ? FolderNameFor(folderName + ".zip") : FolderNameFor(zipPath);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(root, i == 1 ? baseName : baseName + " (" + i + ")");
            if (!Directory.Exists(candidate)) return candidate;
            if (ownerId == null) return candidate; // no owner known (direct use): reuse by name
            var marker = Path.Combine(candidate, OwnerMarkerFile);
            if (ownerId != null && File.Exists(marker))
            {
                try { if (File.ReadAllText(marker).Trim() == ownerId) return candidate; } catch { }
            }
        }
        throw new IOException("Could not find a free folder name for " + baseName + ".");
    }

    private static string Sha256Of(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
    }

    /// Relative path (forward slashes) -> SHA-256 of what Obtainium-PC installed last time.
    private static Dictionary<string, string> ReadManifest(string target)
    {
        try
        {
            var path = Path.Combine(target, ManifestFile);
            if (!File.Exists(path)) return new();
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("files", out var files))
                foreach (var p in files.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? "";
            return map;
        }
        catch { return new(); }
    }

    private static void WriteManifest(string target, string? ownerId, Dictionary<string, string> files)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { owner = ownerId, files });
        File.WriteAllText(Path.Combine(target, ManifestFile), json);
    }

    /// Settings-like files a user may have edited. If one was changed after install it is kept and the
    /// new shipped copy is written next to it as "<name>.new"; programs and libraries are always replaced.
    private static readonly string[] UserEditableExtensions =
        { ".ini", ".cfg", ".conf", ".config", ".json", ".xml", ".yaml", ".yml", ".toml", ".txt", ".db", ".sqlite", ".dat", ".lua", ".cfg" };

    internal static bool IsUserEditable(string rel) =>
        Array.Exists(UserEditableExtensions, e => rel.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static ExtractionResult Extract(string zipPath, string? rootOverride = null, CancellationToken ct = default, string? ownerId = null, string? folderName = null)
    {
        var root = rootOverride ?? DefaultRoot();
        var usedFallback = false;
        try
        {
            var rootIsNew = !Directory.Exists(root);
            Directory.CreateDirectory(root);
            if (rootIsNew && rootOverride == null) RestrictRoot(root);
        }
        catch (Exception ex) when (rootOverride == null
            && (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException))
        {
            // The chosen location is C:\Obtainium: never relocate silently.
            throw new IOException("Could not create \"" + root + "\" (" + ex.Message + "). "
                + "Create that folder yourself or run Obtainium-PC once as administrator, then try again.", ex);
        }

        var target = ChooseTarget(root, zipPath, ownerId, folderName);
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

            MarkFromInternet(staging);
            var stagingRoot = Path.GetFullPath(staging);
            var newManifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
                newManifest[Path.GetRelativePath(stagingRoot, f).Replace('\\', '/')] = Sha256Of(f);
            var targetIsNew = !Directory.Exists(target);
            Directory.CreateDirectory(target);
            // Whatever the parent's inherited permissions are, a folder created here for one app is
            // writable only by this user and administrators (others can read and run).
            if (targetIsNew && rootOverride == null) RestrictRoot(target);
            var kept = MergeInto(staging, target, ct, ownerId, newManifest);
            return new ExtractionResult(target, written, usedFallback, kept);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* leftover staging folder is harmless */ }
        }
    }

    /// Test hook: throw after this many files were moved in, to exercise rollback.
    internal static int? FailAfterMovesForTests;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> TargetLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// Replaces the app's files in place as one unit. Files replaced or removed are first moved to a
    /// backup folder; if anything fails part-way, the new files are removed and the backups are put
    /// back, so the folder is either the old version or the new one, never a mix. Files the previous
    /// install shipped but the new version does not are removed (no stale binaries) unless the user
    /// changed them. Files Obtainium-PC never installed (user data) are never touched. A settings-like
    /// file the user edited is kept and the new copy goes next to it as ".new". Two extractions into the
    /// same target are serialized by a per-target lock. Returns the relative paths of kept user files.
    private static List<string> MergeInto(string staging, string target, CancellationToken ct,
        string? ownerId, Dictionary<string, string> newManifest)
    {
        var gate = TargetLocks.GetOrAdd(Path.GetFullPath(target), _ => new object());
        lock (gate)
        {
            // Read inside the lock so a concurrent extraction cannot leave this view stale.
            var oldManifest = ReadManifest(target);
            var stagingFull = Path.GetFullPath(staging);
            var files = Directory.EnumerateFiles(stagingFull, "*", SearchOption.AllDirectories).ToList();
            string Key(string rel) => rel.Replace('\\', '/');
            bool UserChanged(string rel, string existingPath) =>
                !oldManifest.TryGetValue(Key(rel), out var h) || !string.Equals(h, Sha256Of(existingPath), StringComparison.OrdinalIgnoreCase);

            // Plan: what is replaced, what is kept (as .new), what is stale.
            var plan = new List<(string Src, string Dest, string Rel, bool ReplaceExisting)>();
            var kept = new List<string>();
            foreach (var src in files)
            {
                var rel = Path.GetRelativePath(stagingFull, src);
                var dest = Path.Combine(target, rel);
                if (File.Exists(dest) && IsUserEditable(rel) && UserChanged(rel, dest)
                    && !string.Equals(Sha256Of(dest), Sha256Of(src), StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(Key(rel));
                    plan.Add((src, dest + ".new", rel + ".new", true));
                }
                else plan.Add((src, dest, rel, true));
            }
            var stale = new List<string>();
            foreach (var kv in oldManifest)
            {
                if (newManifest.ContainsKey(kv.Key)) continue;
                var existing = Path.Combine(target, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(existing)) continue;
                // Unchanged since install: obsolete binary, safe to remove. Changed by the user: keep.
                if (string.Equals(Sha256Of(existing), kv.Value, StringComparison.OrdinalIgnoreCase)) stale.Add(existing);
            }

            // Pre-flight: a locked file we are about to replace or remove (the app is running) fails
            // before anything moves.
            foreach (var path in plan.Select(x => x.Dest).Concat(stale))
            {
                if (!File.Exists(path)) continue;
                try { using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("Could not replace \"" + Path.GetRelativePath(target, path) + "\". If the app is running, close it and try again.", ex);
                }
            }

            var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(staging))!, ".backup-" + Guid.NewGuid().ToString("N"));
            var movedIn = new List<string>();
            var backedUp = new List<(string Original, string Saved)>();
            var createdDirs = new List<string>();
            try
            {
                int Backup(string existing)
                {
                    var saved = Path.Combine(backup, Path.GetRelativePath(target, existing));
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Move(existing, saved);
                    backedUp.Add((existing, saved));
                    return 0;
                }
                foreach (var old in stale) { ct.ThrowIfCancellationRequested(); Backup(old); }
                foreach (var (src, dest, _, _) in plan)
                {
                    ct.ThrowIfCancellationRequested();
                    var destDir = Path.GetDirectoryName(dest)!;
                    for (var d = destDir; !Directory.Exists(d); d = Path.GetDirectoryName(d)!) createdDirs.Add(d);
                    Directory.CreateDirectory(destDir);
                    if (File.Exists(dest)) Backup(dest);
                    File.Move(src, dest);
                    movedIn.Add(dest);
                    if (FailAfterMovesForTests is { } n && movedIn.Count >= n)
                        throw new IOException("Injected failure after " + movedIn.Count + " files.");
                }
            }
            catch (Exception ex)
            {
                foreach (var f in movedIn) { try { File.Delete(f); } catch { } }
                foreach (var (original, saved) in backedUp) { try { Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.Move(saved, original, overwrite: true); } catch { } }
                foreach (var d in createdDirs) { try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { } }
                try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
                if (ex is OperationCanceledException) throw;
                if (ex is IOException or UnauthorizedAccessException)
                    throw new IOException("Could not update the app; the previous version was left untouched. If the app is running, close it and try again. (" + ex.Message + ")", ex);
                throw;
            }
            // Success: drop the backup and any folders emptied by removing stale files.
            try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { /* harmless leftover */ }
            foreach (var old in stale)
            {
                try
                {
                    for (var d = Path.GetDirectoryName(old); d != null && d.Length > target.Length && Directory.Exists(d)
                            && !Directory.EnumerateFileSystemEntries(d).Any(); d = Path.GetDirectoryName(d))
                        Directory.Delete(d);
                }
                catch { }
            }
            if (ownerId != null)
            {
                try { File.WriteAllText(Path.Combine(target, OwnerMarkerFile), ownerId); } catch { }
            }
            try { WriteManifest(target, ownerId, newManifest); } catch { }
            return kept;
        }
    }

    /// What a zip holds, decided from its entry names alone (nothing is written).
    public enum ArchiveKind { PortableApp, ContainsInstaller, NoProgram, Unreadable }

    private static readonly System.Text.RegularExpressions.Regex InstallerNameRegex = new(
        @"(^|[^a-z])(setup|install(er)?|uninst(all)?|unins\d*)([^a-z]|$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// PortableApp: has a program (.exe) that is not just an installer, so the zip is the app.
    /// ContainsInstaller: a .msi/.msix/.appx or only setup/installer executables: it is a wrapper
    /// around an installer and must not be treated as an installed app.
    /// NoProgram: no executable at all (data, docs, themes, sources).
    public static ArchiveKind Classify(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName).ToList();
            var packages = names.Any(n => n.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase));
            var exes = names.Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (exes.Count == 0) return packages ? ArchiveKind.ContainsInstaller : ArchiveKind.NoProgram;
            var programs = exes.Where(n => !InstallerNameRegex.IsMatch(Path.GetFileNameWithoutExtension(n))).ToList();
            if (programs.Count == 0) return ArchiveKind.ContainsInstaller;
            // An installer next to program files is still a portable app only if there are real
            // supporting files; a lone msi/exe pair is a wrapper.
            if (packages && names.Count <= 3) return ArchiveKind.ContainsInstaller;
            return ArchiveKind.PortableApp;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return ArchiveKind.Unreadable;
        }
    }

    private static readonly string[] MarkedExtensions =
    {
        ".exe", ".dll", ".msi", ".msix", ".appx", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".lnk", ".scr", ".com", ".cpl", ".hta", ".jar",
    };

    /// Marks extracted executables as downloaded from the internet (Zone.Identifier, ZoneId 3),
    /// exactly as a browser does, so Windows SmartScreen and Smart App Control keep protecting the user.
    /// Best effort: non-NTFS volumes and non-Windows hosts simply skip it.
    internal static void MarkFromInternet(string folder, string? sourceUrl = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        var content = "[ZoneTransfer]\r\nZoneId=3\r\n" + (string.IsNullOrEmpty(sourceUrl) ? "" : "HostUrl=" + sourceUrl + "\r\n");
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (!Array.Exists(MarkedExtensions, e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            try { File.WriteAllText(f + ":Zone.Identifier", content); } catch { }
        }
    }

    /// The Obtainium root holds programs that get run, so only the owner and administrators may write
    /// there; other users can read and run. Applied only to a folder this code just created.
    internal static void RestrictRoot(string root)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var security = new System.Security.AccessControl.DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
            void Allow(System.Security.Principal.SecurityIdentifier sid, System.Security.AccessControl.FileSystemRights rights) =>
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    sid, rights, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null), System.Security.AccessControl.FileSystemRights.FullControl);
            Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null), System.Security.AccessControl.FileSystemRights.FullControl);
            var me = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (me != null) Allow(me, System.Security.AccessControl.FileSystemRights.FullControl);
            Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                System.Security.AccessControl.FileSystemRights.ReadAndExecute);
            new DirectoryInfo(root).SetAccessControl(security);
        }
        catch { /* keep the inherited ACL rather than fail the extraction */ }
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
