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
    public bool HideDowngrades { get; set; }
    public int MinimumUpdateAgeDays { get; set; }
    public int UpdateIntervalMinutes { get; set; } = 360;
    public bool CheckOnStart { get; set; } = true;
    // Register obtainium:// under HKCU\Software\Classes so Obtainium links
    // open in Obtainium-PC (upstream declares the scheme in its manifest).
    public bool HandleObtainiumLinks { get; set; } = true;
    public bool NotifyUpdates { get; set; } = true;
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

    // Update-health detection thresholds (upstream defaults).
    public int UpdateHealthStaleReleaseDays { get; set; } = 180;
    public int UpdateHealthStaleVersionDays { get; set; } = 180;
    public int UpdateHealthFailedChecksCount { get; set; } = 5;
    public bool UpdateHealthArchivedEnabled { get; set; } = true;
    public int UpdateHealthFilteredReleasesCount { get; set; } = 5;
    public bool ShowUpdateHealthIndicator { get; set; } = true;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObtainiumPC");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupted settings fall back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string DataDir => Dir;
}
