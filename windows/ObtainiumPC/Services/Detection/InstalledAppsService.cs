using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ObtainiumPC.Services.Detection;

public sealed record InstalledAppInfo(
    string DisplayName,
    string? DisplayVersion,
    string? InstallLocation,
    string? Publisher);

/// <summary>
/// Windows equivalent of the original's PackageManager lookup: installed
/// programs come from the registry Uninstall keys (HKLM 64-bit, HKLM 32-bit
/// view, HKCU). Matching a tracked app to an installed program is by
/// normalized display name - Windows has no package ID for Win32 apps, so
/// this is a documented best-effort heuristic, exact match first.
/// </summary>
public static class InstalledAppsService
{
    private static readonly Regex NonAlnum = new("[^a-z0-9]+", RegexOptions.Compiled);

    private static string Normalize(string name) => NonAlnum.Replace(name.ToLowerInvariant(), "");

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
        ok &= ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ok &= ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ok &= ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        return new InstalledAppsProbe(results, ok);
    }

    public static IReadOnlyList<InstalledAppInfo> ListInstalledApps()
    {
        var results = new List<InstalledAppInfo>();
        if (!OperatingSystem.IsWindows()) return results;
        ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        return results;
    }

    private static bool ReadHive(RegistryKey root, string path, List<InstalledAppInfo> results)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return false;
            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(subName);
                    var displayName = sub?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    // System components and updates are not user apps.
                    if (sub!.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    results.Add(new InstalledAppInfo(
                        displayName,
                        sub.GetValue("DisplayVersion") as string,
                        sub.GetValue("InstallLocation") as string,
                        sub.GetValue("Publisher") as string));
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
