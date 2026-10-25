using System.Text.Json;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Services.Versioning;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.GitHub;

namespace ObtainiumPC.Services.Updates;

public sealed class MinUpdateAgeException : Exception
{
    public MinUpdateAgeException(DateTimeOffset releaseDate, int minAgeDays)
        : base($"Latest release is younger than the minimum update age ({minAgeDays} days)") { }
}

/// <summary>
/// Ports the add/update pipeline from lib/providers/source_provider.dart (getApp)
/// and the update pass from apps_provider_updates.dart: source resolution,
/// version extraction with global fallback, release-date-as-version, asset
/// filtering with global fallback and arch filter, min-age handling, and
/// install-status reconciliation.
/// </summary>
public sealed class AppUpdateService
{
    private readonly AppSettings _settings;

    public AppUpdateService(AppSettings settings)
    {
        _settings = settings;
    }


    private static string? ExtractVersionWithGlobalDefault(
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        AppSettings settings,
        string stringToCheck,
        bool allowGlobalDefault = true)
    {
        var regEx = additionalSettings.TryGetValue("versionExtractionRegEx", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
        var matchGroup = additionalSettings.TryGetValue("matchGroupToUse", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() : null;
        if (!string.IsNullOrEmpty(regEx) || !allowGlobalDefault)
            return VersionService.ExtractVersion(regEx, matchGroup, stringToCheck);
        try
        {
            return VersionService.ExtractVersion(settings.GlobalVersionExtractionRegEx, settings.GlobalMatchGroupToUse, stringToCheck);
        }
        catch (NoVersionException)
        {
            return null;
        }
    }

    private static bool SettingBool(IReadOnlyDictionary<string, JsonElement> s, string key, bool fallback = false) =>
        s.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.ValueKind == JsonValueKind.True : fallback;

    private static string? SettingString(IReadOnlyDictionary<string, JsonElement> s, string key) =>
        s.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Fetches the latest release for url and returns a new or updated TrackedApp.
    /// currentApp is null when adding.
    /// </summary>
    public async Task<TrackedApp> CheckAppAsync(
        string url,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        TrackedApp? currentApp = null,
        bool trackOnlyOverride = false,
        string? overrideSource = null,
        CancellationToken ct = default)
    {
        var settings = new Dictionary<string, JsonElement>(additionalSettings);
        if (trackOnlyOverride) settings["trackOnly"] = JsonSerializer.SerializeToElement(true);
        var trackOnly = SettingBool(settings, "trackOnly");

        var source = SourceCatalog.Resolve(url, _settings, currentApp?.OverrideSource ?? overrideSource);
        var standardUrl = source.SourceSpecificStandardizeUrl(url);
        var details = await source.GetLatestAssetDetailsAsync(standardUrl, settings, ct);

        // Adding an app honors the minimum update age too.
        if (currentApp == null && !trackOnly)
        {
            var minAgeDays = SettingString(settings, "minimumUpdateAgeDays") is { } raw && int.TryParse(raw, out var p)
                ? p : _settings.MinimumUpdateAgeDays;
            if (details.ReleaseDate is { } rd && minAgeDays > 0 && DateTimeOffset.Now - rd < TimeSpan.FromDays(minAgeDays))
                throw new MinUpdateAgeException(rd, minAgeDays);
        }

        var version = details.Version;
        if (!source.SuppressStandardVersionExtraction)
        {
            var extracted = ExtractVersionWithGlobalDefault(settings, _settings, version);
            if (extracted != null) version = extracted;
        }

        if (SettingBool(settings, "releaseDateAsVersion") && details.ReleaseDate is { } releaseDateForVersion)
            version = TrackedApp.ToMicroseconds(releaseDateForVersion).ToString();

        var assetUrls = details.AssetUrls;
        var filterRegex = SettingString(settings, "apkFilterRegEx") ?? _settings.GlobalAssetFilterRegEx;
        assetUrls = AssetFilterService.FilterAssets(
            assetUrls, filterRegex, SettingBool(settings, "invertAPKFilter") ? true : (bool?)null);
        if (assetUrls.Count == 0 && !trackOnly) throw new NoAppAssetException();

        if (SettingBool(settings, "autoApkFilterByArch"))
        {
            assetUrls = AssetFilterService.FilterAssetsByArch(assetUrls, CurrentArches());
            if (assetUrls.Count == 0 && !trackOnly) throw new NoAppAssetException();
        }

        // Reconcile with the OS-detected installed version (upstream:
        // PackageManager; Windows: registry Uninstall entries). The matcher is
        // conservative: only exact or uniquely-strong name matches (publisher
        // disambiguated) are used; ambiguous results leave the stored version
        // untouched so a wrong match never drives an action.
        if (currentApp != null && !trackOnly && !source.VersionDetectionDisallowed
            && SettingBool(settings, "versionDetection", fallback: true))
        {
            var detected = Detection.InstalledAppsService.FindMatch(currentApp.FinalName, currentApp.Author);
            var realInstalled = detected is { Confidence: >= Detection.InstalledAppsService.MatchConfidence.Strong }
                && detected.Info.DisplayVersion is { Length: > 0 } dv ? dv : null;
            // lib/providers/apps_provider_lifecycle.dart reconcileInstallStatus:
            // adopt the real version when nothing is tracked, then collapse
            // cosmetic differences (leading "v", packaging qualifiers) and
            // adopt latestVersion when the real version is the same release.
            // Without this, "8.9.8.1" vs tag "v8.9.8.1" shows a false update.
            if (detected != null)
            {
                // A Strong+ registry match confirms the install at OS level:
                // record provenance + the exact matched identity for the
                // external-uninstall reconcile (ExternalUninstallService).
                currentApp.InstalledVersionSource = "os";
                currentApp.OsMatchedName = detected.Info.DisplayName;
            }
            if (realInstalled != null && currentApp.InstalledVersion == null)
                currentApp.InstalledVersion = realInstalled;
            var corrected = Versioning.VersionService.ReconcileTrackedVersion(
                currentApp.InstalledVersion, realInstalled, version,
                versionDetectionIsStandard: true,
                naiveStandardVersionDetection: SettingBool(settings, "naiveStandardVersionDetection")
                    || source.NaiveStandardVersionDetection);
            if (corrected != null) currentApp.InstalledVersion = corrected;
        }

        var app = currentApp ?? new TrackedApp();
        app.Url = standardUrl;
        if (overrideSource != null && currentApp == null) app.OverrideSource = overrideSource;
        app.Author = details.Names.Author;
        if (string.IsNullOrWhiteSpace(app.Name)) app.Name = details.Names.Name;
        if (string.IsNullOrEmpty(app.Id)) app.Id = GenerateTempId(standardUrl);
        app.LatestVersion = version;
        app.AssetUrls = assetUrls;
        app.OtherAssetUrls = details.AllAssetUrls;
        app.ReleaseDateUs = details.ReleaseDate is { } rd2 ? TrackedApp.ToMicroseconds(rd2) : null;
        app.ChangeLog = details.ChangeLog;
        app.ReleaseUrl = source.ChangeLogPageFromStandardUrl(standardUrl) ?? standardUrl + "/releases";
        app.IsArchived = details.IsArchived;
        app.NewerReleasesFilteredOut = details.NewerReleasesFilteredOut;
        app.LastUpdateCheckUs = TrackedApp.ToMicroseconds(DateTimeOffset.Now);
        app.ConsecutiveFailedChecks = 0;
        if (currentApp == null) app.AdditionalSettingsJson = JsonSerializer.Serialize(settings);
        if (app.LatestVersionChangedAtUs == null || currentApp?.LatestVersion != version)
            app.LatestVersionChangedAtUs = TrackedApp.ToMicroseconds(DateTimeOffset.Now);
        return app;
    }

    /// Windows CPU architectures in priority order (device arch first).
    public static IReadOnlyList<string> CurrentArches()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => new[] { "x64", "arm64", "x86" },
            System.Runtime.InteropServices.Architecture.Arm64 => new[] { "arm64", "x64", "x86" },
            _ => new[] { "x86", "x64", "arm64" },
        };
    }

    /// Temp ID placeholder for not-yet-installed apps (sha256 prefix), as upstream.
    public static string GenerateTempId(string standardUrl)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(standardUrl));
        return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 12);
    }

    /// Ports applyMinAgeSuppression from utils/min_update_age.dart.
    public static void ApplyMinAgeSuppression(TrackedApp currentApp, TrackedApp fetchedApp)
    {
        fetchedApp.LatestVersion = currentApp.LatestVersion;
        fetchedApp.ReleaseDateUs = currentApp.ReleaseDateUs;
        fetchedApp.ChangeLog = currentApp.ChangeLog;
        fetchedApp.ReleaseUrl = currentApp.ReleaseUrl;
        fetchedApp.AssetUrls = currentApp.AssetUrls;
    }

    public bool IsAppUpdateable(TrackedApp app) =>
        VersionService.IsAppUpdateable(app.InstalledVersion, app.LatestVersion, _settings.HideDowngrades);
}
