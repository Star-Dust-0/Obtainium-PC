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

    public static IReadOnlyList<InstalledAppInfo> ListInstalledApps()
    {
        var results = new List<InstalledAppInfo>();
        if (!OperatingSystem.IsWindows()) return results;
        ReadHive(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ReadHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", results);
        ReadHive(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", results);
        return results;
    }

    private static void ReadHive(RegistryKey root, string path, List<InstalledAppInfo> results)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return;
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
        }
        catch { /* hive unavailable - skip */ }
    }

    /// Best match for a tracked app name: exact normalized match first, then
    /// containment either way (longest candidate wins).
    public static InstalledAppInfo? FindBestMatch(string appName, IReadOnlyList<InstalledAppInfo>? installed = null)
    {
        installed ??= ListInstalledApps();
        var needle = Normalize(appName);
        if (needle.Length == 0) return null;
        var exact = installed.FirstOrDefault(a => Normalize(a.DisplayName) == needle);
        if (exact != null) return exact;
        return installed
            .Where(a =>
            {
                var hay = Normalize(a.DisplayName);
                return hay.Length > 0 && (hay.Contains(needle) || needle.Contains(hay));
            })
            .OrderByDescending(a => Normalize(a.DisplayName).Length)
            .FirstOrDefault();
    }
}
