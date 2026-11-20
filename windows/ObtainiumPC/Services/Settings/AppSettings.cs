using System.Linq;
using System.Text.Json;

namespace ObtainiumPC.Services.Settings;

/// <summary>
/// Global app settings, persisted as JSON next to the data store. Mirrors the
/// Obtainium settings keys that the engine consumes; UI-only settings are added
/// as the pages land.
/// </summary>
public sealed class AppSettings
{
    public string? GlobalVersionExtractionRegEx { get; set; }
    public string? GlobalMatchGroupToUse { get; set; }
    public string? GlobalAssetFilterRegEx { get; set; }
    // lib/main.dart checkAndFlipFirstRun: first run registers the app itself.
    public bool FirstRunHandled { get; set; }
    public bool HideDowngrades { get; set; }
    public int MinimumUpdateAgeDays { get; set; }
    public int UpdateIntervalMinutes { get; set; } = 360;
    public bool CheckOnStart { get; set; } = true;
    // Register obtainium:// under HKCU\Software\Classes so Obtainium links
    // open in Obtainium-PC (upstream declares the scheme in its manifest).
    public bool HandleObtainiumLinks { get; set; } = true;
    public bool NotifyUpdates { get; set; } = true;
    /// Notification-area icon; closing the window then hides it and downloads continue.
    /// Default on when the setting is absent; an explicitly saved value (on or off) is kept.
    public bool TrayIcon { get; set; } = true;
    public string Theme { get; set; } = "system";
    // Upstream stores these as enum indices (settings_provider.dart):
    // sortColumn 0=added 1=nameAuthor 2=authorName 3=releaseDate; sortOrder 0=asc 1=desc.
    public int SortColumn { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool PinUpdates { get; set; } = true;
    public bool BuryNonInstalled { get; set; }
    // lib/providers/settings_provider.dart allowAddingAppsWithoutCheck (default false)
    public bool AllowAddingAppsWithoutCheck { get; set; }
    // Upstream appListDensity: standard / compact / dense (stored as name).
    public string ListDensity { get; set; } = "standard";
    // lib/providers/settings_provider.dart autoExportOnChanges / autoExportFileName
    public bool AutoExportOnChanges { get; set; }
    public string? AutoExportPath { get; set; }
    // lib/providers/settings_provider.dart exportInstalledOnly
    public bool ExportInstalledOnly { get; set; }
    // lib/providers/settings_provider.dart categories: name -> ARGB color int.
    public System.Collections.Generic.Dictionary<string, int> CategoryColors { get; set; } = new();
    // lib/providers/settings_provider.dart categoriesExcludedFromChecks: apps in any of
    // these categories are skipped by update checks (default: none).
    public System.Collections.Generic.HashSet<string> CategoriesExcludedFromChecks { get; set; } = new();
    public bool IsExcludedFromChecks(System.Collections.Generic.IEnumerable<string> appCategories)
        => CategoriesExcludedFromChecks.Count > 0 && appCategories.Any(CategoriesExcludedFromChecks.Contains);
    /// <summary>Upstream setCategories prunes exclusions for removed categories.</summary>
    public void PruneExcludedCategories() => CategoriesExcludedFromChecks.RemoveWhere(c => !CategoryColors.ContainsKey(c));
    // Windows analogue of upstream's Wi-Fi/charging background guards (bg_update_guards.dart):
    // background checks are skipped on a metered connection / on battery power. Both default off;
    // a manual check always runs.
    public bool SkipBackgroundChecksOnMeteredConnection { get; set; }
    public bool SkipBackgroundChecksOnBattery { get; set; }
    public string Language { get; set; } = ""; // "" = follow system (upstream forcedLocale null)
    // lib/providers/settings_provider.dart groupBy: none | category | source.
    public string GroupBy { get; set; } = "none";
    public bool CollapseGroupsOnStartup { get; set; }
    // lib/providers/settings_provider.dart checkUpdateOnDetailPage (default false)
    public bool CheckUpdateOnDetailPage { get; set; }
    // lib/providers/settings_provider.dart onlyCheckInstalledOrTrackOnlyApps (default false)
    public bool OnlyCheckInstalledOrTrackOnlyApps { get; set; }
    // lib/providers/settings_provider.dart includePrereleasesByDefault (default false)
    public bool IncludePrereleasesByDefault { get; set; }
    // lib/providers/settings_provider.dart hideTrackOnlyWarning (default false)
    public bool HideTrackOnlyWarning { get; set; }
    // lib/providers/settings_provider.dart showAppDowngradeError (default true)
    public bool ShowAppDowngradeError { get; set; } = true;
    // lib/providers/settings_provider.dart skipBulkUpdateConfirmation (default false)
    public bool SkipBulkUpdateConfirmation { get; set; }
    // lib/providers/settings_provider.dart parallelDownloads (default true)
    public bool ParallelDownloads { get; set; } = true;
    // lib/providers/settings_provider.dart enableCertificatePinning (default false)
    public bool EnableCertificatePinning { get; set; }
    public string? GitHubPersonalAccessToken { get; set; }
    public bool GitHubCheckRepoRename { get; set; } = true;
    public string? GitHubReqPrefix { get; set; }
    public string? GitLabPersonalAccessToken { get; set; }
    public string? ForgejoPersonalAccessToken { get; set; }

    // lib/providers/settings_provider.dart exportSettings (default 1, values
    // outside 0-2 reset to 1): the manual export level AND the level
    // auto-export-on-changes uses. Level 2 is the only one carrying secrets.
    private int _exportSettingsLevel = 1;
    public int ExportSettingsLevel
    {
        get => _exportSettingsLevel;
        set => _exportSettingsLevel = value is > 2 or < 0 ? 1 : value;
    }

    // Update-health detection thresholds (upstream defaults).
    public int UpdateHealthStaleReleaseDays { get; set; } = 180;
    public int UpdateHealthStaleVersionDays { get; set; } = 180;
    public int UpdateHealthFailedChecksCount { get; set; } = 5;
    public bool UpdateHealthArchivedEnabled { get; set; } = true;
    public int UpdateHealthFilteredReleasesCount { get; set; } = 5;
    public bool ShowUpdateHealthIndicator { get; set; } = true;

    /// Tests point the settings file elsewhere; production leaves this null.
    public static string? DirOverride { get; set; }

    /// True when settings.json existed but could not be read. Saves are blocked in that
    /// state (same rule as apps.json) so the user's file is never replaced by defaults.
    public static bool LoadFailed { get; private set; }
    public static string? LoadBackupPath { get; private set; }

    private static string Dir =>
        DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObtainiumPC");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        LoadFailed = false; LoadBackupPath = null;
        try
        {
            if (File.Exists(FilePath))
                return FromDisk(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            // Corrupted settings fall back to defaults, but the unreadable file is
            // kept: the next Save would otherwise overwrite the user's tokens/options.
            LoadFailed = true;
            try
            {
                LoadBackupPath = FilePath + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(FilePath, LoadBackupPath, overwrite: false);
            }
            catch { LoadBackupPath = null; }
            Logging.AppLogger.Error("settings.json is unreadable; defaults are used and a copy of the file was kept", e);
        }
        return new AppSettings();
    }

    public void Save()
    {
        if (LoadFailed)
        {
            Logging.AppLogger.Warn("Skipping settings save: settings.json failed to load this session, so overwriting it is blocked." +
                (LoadBackupPath != null ? " Preserved copy: " + LoadBackupPath : ""));
            return;
        }
        lock (SaveLock)
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, ToDisk());
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }

    /// Tokens are encrypted with the Windows user's DPAPI key in settings.json only. In memory
    /// (and so in every export) they stay in clear: an export has to restore on another machine.
    [System.Text.Json.Serialization.JsonIgnore]
    internal Dictionary<string, string> LockedSecrets { get; set; } = new();

    internal string ToDisk()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.GitHubPersonalAccessToken = ProtectOrKeep(GitHubPersonalAccessToken, nameof(GitHubPersonalAccessToken));
        copy.GitLabPersonalAccessToken = ProtectOrKeep(GitLabPersonalAccessToken, nameof(GitLabPersonalAccessToken));
        copy.ForgejoPersonalAccessToken = ProtectOrKeep(ForgejoPersonalAccessToken, nameof(ForgejoPersonalAccessToken));
        return JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
    }

    // A token that is empty in memory because it could not be decrypted is written back as the
    // original blob; a token the user typed since replaces it.
    private string? ProtectOrKeep(string? value, string name) =>
        string.IsNullOrEmpty(value) && LockedSecrets.TryGetValue(name, out var raw) ? raw : SecretProtector.Protect(value);

    internal static AppSettings FromDisk(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        s.GitHubPersonalAccessToken = s.Open(s.GitHubPersonalAccessToken, nameof(GitHubPersonalAccessToken));
        s.GitLabPersonalAccessToken = s.Open(s.GitLabPersonalAccessToken, nameof(GitLabPersonalAccessToken));
        s.ForgejoPersonalAccessToken = s.Open(s.ForgejoPersonalAccessToken, nameof(ForgejoPersonalAccessToken));
        return s;
    }

    private string? Open(string? stored, string name)
    {
        if (SecretProtector.TryUnprotect(stored, out var plain)) return plain;
        LockedSecrets[name] = stored!;
        return null;
    }

    // Saves come from the UI thread and from background checks; one tmp file must not be shared by two writers.
    private static readonly object SaveLock = new();

    public static string DataDir => Dir;

    /// A proxy prefix is stored as a bare host/path: no scheme, no trailing slash.
    public static string? CleanPrefix(string? v)
    {
        var t = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        if (t == null) return null;
        foreach (var scheme in new[] { "https://", "http://" })
            if (t.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) { t = t.Substring(scheme.Length); break; }
        t = t.TrimEnd('/');
        return t.Length == 0 ? null : t;
    }
}


/// DPAPI (current user) protection for stored tokens. Values written by older versions are plain
/// text and are read as they are; they are encrypted on the next save. A value that cannot be
/// decrypted (another Windows account or machine) reads as empty so the user is asked for it again.
internal static class SecretProtector
{
    private const string Prefix = "dpapi1:";

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;
        var bytes = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plain), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? stored) => TryUnprotect(stored, out var plain) ? plain : null;

    /// False only for a protected value this user cannot decrypt; the caller keeps the raw
    /// value so a later save writes it back unchanged instead of losing the credential.
    public static bool TryUnprotect(string? stored, out string? plain)
    {
        plain = stored;
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(stored.Substring(Prefix.Length)), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            plain = System.Text.Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (Exception) { plain = null; return false; }
    }
}
