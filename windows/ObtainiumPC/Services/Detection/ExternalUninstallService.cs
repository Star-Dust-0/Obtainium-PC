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

    // No Clear action exists: complete registry absence plus no name match is
    // inference, not proof of uninstall (per-user/machine hive blind spots,
    // MSIX, portable installs, renames). SuspectedGone is a separately
    // retained status - the recorded installed state is never mutated.
    public enum ReconcileAction { SuspectedGone, Readopt }

    public readonly record struct ReconcileResult(string Id, ReconcileAction Action, string? NewMatchedName);

    /// <summary>
    /// Absence of the recorded identity is an inference, not proof of
    /// uninstall: a program renamed across versions ("Foo 1.2" -> "Foo 1.3")
    /// drops its old DisplayName while staying installed. So before clearing,
    /// re-run the app-name matcher (the same conservative tiers detection
    /// uses); a Strong+ match means the app is still installed under a new
    /// identity - re-adopt that identity instead of clearing. Only when the
    /// exact recorded identity is absent AND the matcher finds nothing does
    /// the record get cleared. The caller supplies the matcher (registry
    /// types stay out of this pure layer) and the complete-probe guarantee.
    /// </summary>
    public static List<ReconcileResult> Reconcile(
        IEnumerable<TrackedApp> apps,
        IReadOnlyCollection<string> installedDisplayNames,
        Func<TrackedApp, bool> detectionAllowed,
        Func<TrackedApp, string?> findCurrentMatch)
    {
        var present = installedDisplayNames
            .Select(Normalize)
            .ToHashSet();
        var results = new List<ReconcileResult>();
        foreach (var app in apps)
        {
            if (app.InstalledVersion == null) continue;
            if (app.InstalledVersionSource != "os") continue;
            if (string.IsNullOrEmpty(app.OsMatchedName)) continue;
            if (!detectionAllowed(app)) continue;
            if (present.Contains(Normalize(app.OsMatchedName))) continue;
            // Recorded identity is gone. Re-adoption requires an UNAMBIGUOUS
            // current identity (the caller's matcher enforces exact or
            // publisher-disambiguated matches, not only a Strong name score).
            var current = findCurrentMatch(app);
            if (current != null)
            {
                results.Add(new ReconcileResult(app.Id, ReconcileAction.Readopt, current));
                continue;
            }
            results.Add(new ReconcileResult(app.Id, ReconcileAction.SuspectedGone, null));
        }
        return results;
    }
}
