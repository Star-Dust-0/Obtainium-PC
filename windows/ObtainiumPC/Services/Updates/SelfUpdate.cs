using System.Text.Json;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Updates;

/// <summary>
/// Self-update, the Windows form of upstream's lib/main.dart first-run
/// bootstrap: on the very first run Obtainium registers ITSELF as a tracked
/// app (its own repo URL, installedVersion = the running version), so it is
/// checked and updated through the same pipeline as every other app. As in
/// lib/providers/apps_provider_install.dart ("move Obtainium to the end of
/// the line"), the own installer is always launched after every other app
/// has been handled.
/// </summary>
public static class SelfUpdate
{
    public const string OwnUrl = "https://github.com/Star-Dust-0/Obtainium-PC";
    public const string OwnId = "starsdust01.obtainium-pc";
    // Records written by builds from before the repository moved to the new
    // account; still recognised as the own app and moved to OwnUrl on start.
    public const string LegacyOwnUrl = "https://github.com/MohamedElnaggar00/Obtainium-PC";
    public const string LegacyOwnId = "mohamedelnaggar00.obtainium-pc";
    public const string OwnName = "Obtainium-PC";
    public const string OwnAuthor = "Star-Dust-0";
    // The installer asset published by CI is ObtainiumPC-Setup-win-x64.exe
    // (windows/packaging/ObtainiumPC.iss OutputBaseFilename).
    public const string InstallerAssetFilter = "Setup";

    public static bool IsSelf(TrackedApp app) =>
        app.Id == OwnId || app.Id == LegacyOwnId || SameUrl(app.Url, OwnUrl) || SameUrl(app.Url, LegacyOwnUrl);

    private static bool SameUrl(string? a, string b) =>
        string.Equals((a ?? "").Trim().TrimEnd('/'), b, StringComparison.OrdinalIgnoreCase);

    public static string RunningVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}"
            : "0.0.0";

    /// <summary>
    /// Adds the own-app record when it is not tracked yet. Returns the added
    /// record, or null when one already exists (never duplicates or edits an
    /// existing record, never removes anything).
    /// </summary>
    public static TrackedApp? EnsureTracked(List<TrackedApp> apps, string runningVersion,
        string? osMatchedName = null)
    {
        if (apps.Any(IsSelf)) return null;
        var settings = new Dictionary<string, object?>
        {
            ["versionDetection"] = true,
            ["apkFilterRegEx"] = InstallerAssetFilter,
            ["invertAPKFilter"] = false,
            // Releases are prereleases while the app is in trial; stable releases are still found.
            ["includePrereleases"] = true,
        };
        var app = new TrackedApp
        {
            Id = OwnId,
            Url = OwnUrl,
            Author = OwnAuthor,
            Name = OwnName,
            InstalledVersion = runningVersion,
            InstalledVersionSource = osMatchedName != null ? "os" : "manual",
            OsMatchedName = osMatchedName,
            LatestVersion = runningVersion,
            AdditionalSettingsJson = JsonSerializer.Serialize(settings),
        };
        apps.Add(app);
        return app;
    }

    /// <summary>
    /// One-time compatible migration for own-app records created before
    /// prereleases were included: only when the key is ABSENT is
    /// includePrereleases set to true. An explicit user choice (true or
    /// false) is never touched, nor is any other setting. Returns whether a
    /// record changed.
    /// </summary>
    public static bool MigrateOwnRecord(List<TrackedApp> apps)
    {
        var changed = false;
        foreach (var app in apps.Where(IsSelf))
        {
            if (SameUrl(app.Url, LegacyOwnUrl)) { app.Url = OwnUrl; app.Author = OwnAuthor; changed = true; }
            if (app.AdditionalSettings.ContainsKey("includePrereleases")) continue;
            app.SetSetting("includePrereleases", true);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// The running build reports "0.1.0" (assembly version, three parts) while
    /// the release tag is "v0.1". Same numeric release, different spelling, so
    /// the app must not offer itself as an update to itself. Only the own app
    /// uses this; other apps keep the exact string comparison. Words such as
    /// beta or rc stay significant, so a real newer or pre-release tag still
    /// counts as an update. Nothing stored is rewritten.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex VersionPrefix =
        new(@"^\s*(?:version|ver|release|v)?[\s._\-]*(?=\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Drops a cosmetic lead-in such as "v", "V", "v.", "version " or "release " before the first digit.</summary>
    public static string StripPrefix(string v) => VersionPrefix.Replace(v.Trim(), "", 1);

    public static bool SameRelease(string? installed, string? latest)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(latest)) return false;
        var a = Versioning.VersionNormalization.NormalizeVersionForComparison(StripPrefix(installed)).Split('.').ToList();
        var b = Versioning.VersionNormalization.NormalizeVersionForComparison(StripPrefix(latest)).Split('.').ToList();
        while (a.Count > 1 && a[^1].Length > 0 && a[^1].All(c => c == '0')) a.RemoveAt(a.Count - 1);
        while (b.Count > 1 && b[^1].Length > 0 && b[^1].All(c => c == '0')) b.RemoveAt(b.Count - 1);
        return a.Count > 0 && a[0].Length > 0 && a.SequenceEqual(b);
    }

    /// <summary>Update decision: same-release equivalence for every app (prefix, trailing .0 parts), exact otherwise.</summary>
    public static bool IsUpdateable(TrackedApp app, bool hideDowngrades) =>
        !SameRelease(app.InstalledVersion, app.LatestVersion)
        && Versioning.VersionService.IsAppUpdateable(app.InstalledVersion, app.LatestVersion, hideDowngrades);

    /// <summary>The recorded install already is the latest release (exact match, or same release for the own app).</summary>
    public static bool VersionsMatch(TrackedApp app) =>
        app.InstalledVersion == app.LatestVersion || SameRelease(app.InstalledVersion, app.LatestVersion);

    /// <summary>Detail-page action decision with the same own-app equivalence as the list.</summary>
    public static (bool Enabled, bool FirstInstall) DetailAction(TrackedApp app)
    {
        var (enabled, first) = Versioning.VersionService.DetailAction(app.InstalledVersion, app.LatestVersion);
        if (enabled && !first && VersionsMatch(app)) return (false, false);
        return (enabled, first);
    }

    /// <summary>Stable order with the own app moved to the end.</summary>
    public static List<T> OrderSelfLast<T>(IEnumerable<T> items, Func<T, TrackedApp> app) =>
        items.Where(i => !IsSelf(app(i))).Concat(items.Where(i => IsSelf(app(i)))).ToList();
}
