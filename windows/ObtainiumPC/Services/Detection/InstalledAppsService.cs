using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ObtainiumPC.Services.Detection;

public sealed record InstalledAppInfo(
    string DisplayName,
    string? DisplayVersion,
    string? InstallLocation,
    string? Publisher,
    string? KeyName = null,
    string? DisplayIcon = null);

/// <summary>
/// Windows equivalent of the original's PackageManager lookup: installed
/// programs come from the registry Uninstall keys (HKLM 64-bit, HKLM 32-bit
/// view, HKCU). Matching a tracked app to an installed program is by
/// normalized display name - Windows has no package ID for Win32 apps, so
/// this is a documented best-effort heuristic, exact match first.
/// </summary>
public static class InstalledAppsService
{
    // Explicit 64-bit views: the 32-bit app list is read from WOW6432Node by name, so the result no
    // longer depends on whether this process is 32-bit or 64-bit.
    private static readonly RegistryKey Hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    private static readonly RegistryKey Hkcu64 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
    private static readonly Regex NonAlnum = new("[^a-z0-9]+", RegexOptions.Compiled);

    // "+" is spelled out so "notepad-plus-plus" (repository name) and
    // "Notepad++" (registry DisplayName) normalize to the same key.
    private static string Normalize(string name) => NonAlnum.Replace(name.ToLowerInvariant().Replace("+", "plus"), "");

    private static readonly Regex TrailingDigits = new("[0-9]+$", RegexOptions.Compiled);

    public static string NormalizeName(string name) => Normalize(name);

    public sealed record InstalledAppsProbe(IReadOnlyList<InstalledAppInfo> Apps, bool AllHivesRead);

    /// <summary>
    /// Lists installed programs and reports whether EVERY hive was readable.
    /// External-uninstall decisions require a complete probe: a DisplayName
    /// missing from a partial listing proves nothing.
    /// </summary>
    public static InstalledAppsProbe ProbeInstalledApps()
    {
        if (!OperatingSystem.IsWindows()) return new InstalledAppsProbe(new List<InstalledAppInfo>(), false);
        var results = new List<InstalledAppInfo>();
        var ok = true;
        ok &= ReadHive(Hklm64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ok &= ReadHive(Hklm64, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ok &= ReadHive(Hkcu64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        return new InstalledAppsProbe(Dedupe(results), ok);
    }

    public static IReadOnlyList<InstalledAppInfo> ListInstalledApps()
    {
        if (!OperatingSystem.IsWindows()) return new List<InstalledAppInfo>();
        lock (CacheGate)
        {
            // Every match used to re-read three registry hives; a burst of matches (list refresh,
            // install watcher) now shares one read for a few seconds.
            if (_cache != null && DateTimeOffset.Now - _cacheAt < CacheTtl) return _cache;
            var results = new List<InstalledAppInfo>();
            ReadHive(Hklm64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
            ReadHive(Hklm64, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
            ReadHive(Hkcu64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
            _cache = Dedupe(results); _cacheAt = DateTimeOffset.Now;
            return _cache;
        }
    }

    private static readonly Regex UpdateName = new(@"\bKB\d{6,8}\b|^(Security Update|Hotfix|Update) for ", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// Windows hotfixes and component updates live in the same Uninstall list as programs. They
    /// are never a tracked app, and their many near-identical names make real matches ambiguous.
    public static bool IsUpdateEntry(string displayName, string? releaseType, string? parentKeyName)
        => !string.IsNullOrEmpty(parentKeyName)
           || (releaseType != null && (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
           || UpdateName.IsMatch(displayName);

    /// The same program is often listed in both the 64-bit and WOW6432Node (or HKCU) views.
    /// Identical name, version and publisher is one program, not an ambiguity.
    public static List<InstalledAppInfo> Dedupe(IEnumerable<InstalledAppInfo> all)
    {
        var seen = new HashSet<string>();
        var result = new List<InstalledAppInfo>();
        foreach (var a in all)
            if (seen.Add(Normalize(a.DisplayName) + "|" + (a.DisplayVersion ?? "") + "|" + Normalize(a.Publisher ?? ""))) result.Add(a);
        return result;
    }

    private static readonly object CacheGate = new();
    private static IReadOnlyList<InstalledAppInfo>? _cache;
    private static DateTimeOffset _cacheAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);
    /// Forget the cached list (after an install the next read must see the new registry state).
    public static void InvalidateCache() { lock (CacheGate) _cache = null; }

    private static bool ReadHive(RegistryKey root, string path, List<InstalledAppInfo> results)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            // A missing Uninstall key (e.g. no per-user installs under HKCU) is an empty list, not a failed read.
            if (key == null) return true;
            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var displayName = sub?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    // System components and updates are not user apps.
                    if (sub!.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (IsUpdateEntry(displayName, sub.GetValue("ReleaseType") as string, sub.GetValue("ParentKeyName") as string)) continue;
                    results.Add(new InstalledAppInfo(
                        displayName,
                        sub.GetValue("DisplayVersion") as string,
                        sub.GetValue("InstallLocation") as string,
                        sub.GetValue("Publisher") as string,
                        subName,
                        sub.GetValue("DisplayIcon") as string));
                }
                catch { /* unreadable subkey - skip */ }
            }
            return true;
        }
        catch { /* hive unavailable - incomplete probe */ return false; }
    }

    public enum MatchConfidence { None, Weak, Strong, Exact }

    public sealed record MatchResult(InstalledAppInfo Info, MatchConfidence Confidence);

    /// <summary>
    /// Matches a tracked app to an installed program with explicit confidence.
    /// Windows has no package ID for Win32 apps, so identity must be
    /// conservative: only Exact and Strong matches may drive anything
    /// user-visible; Weak/Ambiguous results are returned as null so a wrong
    /// match can never drive an action. Order of attempts:
    /// 1. Exact normalized display-name match (publisher disambiguates ties).
    /// 2. Unique prefix match, minimum normalized length of 4 (covers arch /
    ///    edition suffixes like "Notepad++ (64-bit x64)").
    /// 3. Unique containment match with at least 60% length overlap and a
    ///    minimum normalized length of 4, publisher disambiguates ties.
    /// Anything ambiguous returns null.
    /// </summary>
    public static InstalledAppInfo? FindBestMatch(string appName, string? publisherHint = null, IReadOnlyList<InstalledAppInfo>? installed = null)
        => FindMatch(appName, publisherHint, installed)?.Info;

    public static MatchResult? FindMatch(string appName, string? publisherHint = null, IReadOnlyList<InstalledAppInfo>? installed = null)
    {
        installed ??= ListInstalledApps();
        var needle = Normalize(appName);
        if (needle.Length == 0) return null;
        var publisher = string.IsNullOrWhiteSpace(publisherHint) ? null : Normalize(publisherHint);

        InstalledAppInfo? Disambiguate(List<InstalledAppInfo> candidates)
        {
            if (candidates.Count == 1) return candidates[0];
            if (publisher != null)
            {
                var byPublisher = candidates
                    .Where(c => c.Publisher != null && Normalize(c.Publisher).Contains(publisher))
                    .ToList();
                if (byPublisher.Count == 1) return byPublisher[0];
            }
            return null; // ambiguous - never guess
        }

        var exact = installed.Where(a => Normalize(a.DisplayName) == needle).ToList();
        var exactMatch = Disambiguate(exact);
        if (exactMatch != null) return new MatchResult(exactMatch, MatchConfidence.Exact);

        // Identity tier: the uninstall key name, the install folder name or the
        // icon's exe name equals the tracked name (installers whose DisplayName
        // carries extra words, e.g. "Shuck - Screen OCR").
        if (needle.Length >= 4)
        {
            var byIdentity = installed.Where(a =>
                (a.KeyName != null && Normalize(a.KeyName) == needle)
                || (!string.IsNullOrWhiteSpace(a.InstallLocation) && Normalize(Path.GetFileName(a.InstallLocation.TrimEnd('\\', '/'))) == needle)
                || (!string.IsNullOrWhiteSpace(a.DisplayIcon) && Normalize(Path.GetFileNameWithoutExtension(a.DisplayIcon.Split(',')[0].Trim('"'))) == needle)).ToList();
            var idMatch = Disambiguate(byIdentity);
            if (idMatch != null) return new MatchResult(idMatch, MatchConfidence.Strong);
        }

        // Owner tier: some programs are named after their GitHub owner, not the
        // repository (BCUninstaller/Bulk-Crap-Uninstaller installs as
        // "BCUninstaller 6.3.0.0"). Matches only when the DisplayName with its
        // trailing version digits removed equals the tracked author exactly,
        // and only when that is unique. No hard-coded app names.
        if (publisher != null && publisher.Length >= 6)
        {
            var byOwner = installed.Where(a => TrailingDigits.Replace(Normalize(a.DisplayName), "") == publisher).ToList();
            if (byOwner.Count == 1) return new MatchResult(byOwner[0], MatchConfidence.Strong);
        }

        if (needle.Length < 4) return null; // too short to contain-match safely

        // Prefix tier: "notepad" is a prefix of "notepad64bitx64" (arch and
        // edition suffixes in DisplayName). Stronger than arbitrary
        // containment, so no length-overlap requirement beyond the minimum.
        var prefix = installed
            .Where(a =>
            {
                var hay = Normalize(a.DisplayName);
                return hay.Length >= 4 && (hay.StartsWith(needle) || needle.StartsWith(hay));
            })
            .ToList();
        var prefixMatch = Disambiguate(prefix);
        if (prefixMatch != null) return new MatchResult(prefixMatch, MatchConfidence.Strong);

        var containment = installed
            .Where(a =>
            {
                var hay = Normalize(a.DisplayName);
                if (hay.Length == 0 || !(hay.Contains(needle) || needle.Contains(hay))) return false;
                var shorter = Math.Min(hay.Length, needle.Length);
                var longer = Math.Max(hay.Length, needle.Length);
                return shorter >= 4 && shorter * 5 >= longer * 3; // >= 60% overlap
            })
            .ToList();
        var containmentMatch = Disambiguate(containment);
        return containmentMatch != null ? new MatchResult(containmentMatch, MatchConfidence.Strong) : null;
    }
}
