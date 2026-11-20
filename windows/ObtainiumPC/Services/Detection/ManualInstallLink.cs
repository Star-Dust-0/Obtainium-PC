using ObtainiumPC.Models;

namespace ObtainiumPC.Services.Detection;

/// <summary>
/// Manual link between a tracked app and an installed Windows program. Windows has no package id, so the
/// automatic name match can miss (renamed DisplayName, several similar programs). The user picks the program;
/// its uninstall key is remembered (FindMatchFor tries it first) and its version becomes the installed version.
/// </summary>
public static class ManualInstallLink
{
    /// Programs that can be linked: those with an uninstall key, filtered by a name substring, sorted by name.
    public static List<InstalledAppInfo> Candidates(IEnumerable<InstalledAppInfo> installed, string? filter)
    {
        var f = (filter ?? "").Trim();
        return installed
            .Where(a => !string.IsNullOrEmpty(a.KeyName) && !string.IsNullOrWhiteSpace(a.DisplayName))
            .Where(a => f.Length == 0 || a.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Links the app to the program. Returns false (and changes nothing) when the program has no key or no version.
    public static bool Apply(TrackedApp app, InstalledAppInfo program)
    {
        if (string.IsNullOrEmpty(program.KeyName) || string.IsNullOrWhiteSpace(program.DisplayVersion)) return false;
        app.SetSetting(InstalledAppsService.InstalledKeySetting, program.KeyName);
        app.InstalledVersion = program.DisplayVersion;
        app.InstalledVersionSource = "os";
        app.OsMatchedName = program.DisplayName;
        return true;
    }

    /// Forgets the remembered program (used by "Reset install status").
    public static void Clear(TrackedApp app) => app.SetSetting(InstalledAppsService.InstalledKeySetting, null);
}
