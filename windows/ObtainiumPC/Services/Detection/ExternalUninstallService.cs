using System;
using System.Collections.Generic;
using System.Linq;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Detection;

/// <summary>
/// lib/providers/apps_provider_lifecycle.dart reconcileInstallStatus: a
/// tracked app whose recorded install is no longer reported by the OS gets
/// its installedVersion cleared (upstream then optionally deletes the record
/// under removeOnExternalUninstall; this port never deletes records - the
/// destructive half is disclosed in PARITY.md).
///
/// Windows safety rules (registry non-match is not proof of uninstall):
/// - Only records whose install was OS-confirmed (InstalledVersionSource ==
///   "os", set when registry detection adopted/corrected the version) are
///   eligible. Manual marks and legacy records are never touched.
/// - The exact DisplayName matched at confirmation time (OsMatchedName)
///   must be absent from a COMPLETE registry probe (all hives read).
///   Identity, not fuzzy matching, drives the clear.
/// - The caller passes detection gating (track-only, versionDetection off,
///   source-disallowed detection) as a predicate; gated apps are preserved.
/// </summary>
public static class ExternalUninstallService
{
    // Same normalization InstalledAppsService applies (kept local so this
    // file stays WinRT/Registry-free and unit-testable).
    private static readonly System.Text.RegularExpressions.Regex NonAlnum =
        new("[^a-z0-9]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string Normalize(string name) => NonAlnum.Replace(name.ToLowerInvariant(), "");

    /// <summary>
    /// Returns the ids whose recorded OS-confirmed install is verifiably
    /// gone. Caller clears InstalledVersion for exactly these and persists.
    /// </summary>
    public static List<string> FindExternallyRemoved(
        IEnumerable<TrackedApp> apps,
        IReadOnlyCollection<string> installedDisplayNames,
        Func<TrackedApp, bool> detectionAllowed)
    {
        var present = installedDisplayNames
            .Select(Normalize)
            .ToHashSet();
        var removed = new List<string>();
        foreach (var app in apps)
        {
            if (app.InstalledVersion == null) continue;
            if (app.InstalledVersionSource != "os") continue;
            if (string.IsNullOrEmpty(app.OsMatchedName)) continue;
            if (!detectionAllowed(app)) continue;
            if (present.Contains(Normalize(app.OsMatchedName))) continue;
            removed.Add(app.Id);
        }
        return removed;
    }
}
