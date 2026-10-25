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
    public const string OwnUrl = "https://github.com/MohamedElnaggar00/Obtainium-PC";
    public const string OwnId = "mohamedelnaggar00.obtainium-pc";
    public const string OwnName = "Obtainium-PC";
    public const string OwnAuthor = "MohamedElnaggar00";
    // The installer asset published by CI is ObtainiumPC-Setup-win-x64.exe
    // (windows/packaging/ObtainiumPC.iss OutputBaseFilename).
    public const string InstallerAssetFilter = "Setup";

    public static bool IsSelf(TrackedApp app) =>
        app.Id == OwnId || SameUrl(app.Url, OwnUrl);

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

    /// <summary>Stable order with the own app moved to the end.</summary>
    public static List<T> OrderSelfLast<T>(IEnumerable<T> items, Func<T, TrackedApp> app) =>
        items.Where(i => !IsSelf(app(i))).Concat(items.Where(i => IsSelf(app(i)))).ToList();
}
