using ObtainiumPC.Sources;

namespace ObtainiumPC.Sources.Forms;

public enum OptionFormItemKind { Header, Switch, Text, ChoiceList }

/// <summary>
/// A single per-app options form item. Ports GeneratedFormItem
/// (lib/components/generated_form_model.dart) to a plain spec the WinUI page renders.
/// Keys match the original additionalSettings keys exactly for import compatibility.
/// </summary>
public sealed class OptionFormItem
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required OptionFormItemKind Kind { get; init; }
    public bool DefaultBool { get; set; }
    public string? DefaultString { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Options { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public string? Hint { get; init; }
    public bool Disabled { get; set; }
    public bool ValidateRegex { get; init; }
    /// Custom value codec for structured settings (intermediateLink, requestHeader).
    public string? SpecialCodec { get; init; }
}

/// <summary>
/// Ports combinedAppSpecificSettingFormItems from lib/app_sources/app_source.dart
/// plus updateHealthOverrideFormItems from lib/utils/update_health.dart.
/// Labels use the upstream English strings; Android-only wording is mapped to the
/// Windows equivalent approved in the concept review. Android-only options with no Windows
/// analog are not shown (their stored values are kept in the records untouched).
/// </summary>
public static class AppOptionFormSpec
{
    private static readonly int[] MinimumUpdateAgeOptions = { 0, 1, 2, 3, 5, 7, 14, 30 };
    private static readonly int[] UpdateHealthDayOptions = { 0, 30, 60, 90, 180, 365, 730 };
    private static readonly int[] UpdateHealthFailedChecksOptions = { 0, 1, 2, 3, 5, 8, 10, 15, 20 };
    private static readonly int[] UpdateHealthFilteredReleasesOptions = { 0, 1, 2, 3, 5, 10, 20, 50 };

    private static OptionFormItem Header(string label, string key = "") =>
        new() { Key = key, Label = label, Kind = OptionFormItemKind.Header };

    private static OptionFormItem Switch(string key, string label, bool value = false, bool disabled = false) =>
        new() { Key = key, Label = label, Kind = OptionFormItemKind.Switch, DefaultBool = value, Disabled = disabled };

    private static OptionFormItem Text(string key, string label, string? hint = null, bool validateRegex = false) =>
        new() { Key = key, Label = label, Kind = OptionFormItemKind.Text, Hint = hint, ValidateRegex = validateRegex };

    private static OptionFormItem Choice(string key, string label, IEnumerable<KeyValuePair<string, string>> options, string defaultValue = "") =>
        new() { Key = key, Label = label, Kind = OptionFormItemKind.ChoiceList, Options = options.ToList(), DefaultString = defaultValue };

    private static string DaysLabel(int days) => days == 1 ? "1 day" : $"{days} days";

    private static IReadOnlyList<KeyValuePair<string, string>> ThresholdOptions(IEnumerable<int> values, Func<int, string> label)
    {
        var list = new List<KeyValuePair<string, string>>
        {
            new("", "Use global default"),
            new("off", "Disabled"),
        };
        foreach (var v in values.Where(v => v > 0))
            list.Add(new(v.ToString(), label(v)));
        return list;
    }

    public static IReadOnlyList<IReadOnlyList<OptionFormItem>> Build(AppSourceBase source)
    {
        var excluded = new HashSet<string>(source.ExcludeCommonSettingKeys);

        // ---- Version detection ----
        var version = new List<OptionFormItem>
        {
            Text("versionExtractionRegEx", "Trim version string with RegEx", validateRegex: true),
            Text("matchGroupToUse", "Match group to use for \"Trim version string with RegEx\"", hint: "$0"),
            Switch("versionDetection", "Reconcile version string with version detected from OS", value: true),
        };
        if (source.ShowReleaseDateAsVersionToggle)
            version.Add(Switch("releaseDateAsVersion", "Use release date as version string (pseudo-version)"));
        version.Add(Switch("useVersionCodeAsOSVersion", "Use file version as OS-detected version"));
        if (source.VersionDetectionDisallowed)
            foreach (var item in version.Where(i => i.Key is "versionDetection" or "useVersionCodeAsOSVersion"))
            {
                item.Disabled = true;
                item.DefaultBool = false;
            }
        version.RemoveAll(i => excluded.Contains(i.Key));

        // ---- Asset selection (upstream: APK selection) ----
        var assets = new List<OptionFormItem>
        {
            Text("apkFilterRegEx", "Filter assets by regular expression", validateRegex: true),
            Switch("invertAPKFilter", "Invert regular expression (Filter assets by regular expression)"),
            Switch("autoApkFilterByArch", "Attempt to filter assets by CPU architecture if possible", value: true),
        };
        if (source.AllowIncludeZips)
        {
            assets.Add(Switch("includeZips", "Include ZIP files"));
            assets.Add(Text("zippedApkFilterRegEx", "Filter assets inside ZIP", validateRegex: true));
        }
        if (source.AllowIncludeTarballs)
        {
            assets.Add(Switch("includeTarballs", "Include tarball files (tar.gz, etc.)"));
            assets.Add(Text("tarballedApkFilterRegEx", "Filter assets inside tarball", validateRegex: true));
        }
        assets.RemoveAll(i => excluded.Contains(i.Key));

        // ---- Update options ----
        var minAgeOptions = new List<KeyValuePair<string, string>> { new("", "Use global default") };
        foreach (var d in MinimumUpdateAgeOptions)
            minAgeOptions.Add(new(d.ToString(), d == 0 ? "None" : DaysLabel(d)));
        var update = new List<OptionFormItem>
        {
            Choice("minimumUpdateAgeDays", "Minimum age for updates", minAgeOptions),
            Switch("exemptFromBackgroundUpdates", "Exempt from background updates (if enabled)"),
            Switch("skipUpdateNotifications", "Skip update notifications"),
            Switch("refreshBeforeDownload", "Refresh app details before download"),
        };
        update.RemoveAll(i => excluded.Contains(i.Key));

        // ---- App options ----
        var app = new List<OptionFormItem>
        {
            Switch("trackOnly", "Track-only"),
            Text("appName", "App name"),
            Text("appAuthor", "Author"),
            Switch("allowInsecure", "Allow insecure HTTP requests"),
            Text("allowedSigningCertHashes", "Expected signing certificate hashes", hint: "AA:BB:CC:..."),
            Text("about", "About"),
        };
        app.RemoveAll(i => excluded.Contains(i.Key));

        // ---- Update health overrides (lib/utils/update_health.dart) ----
        var health = new List<OptionFormItem>
        {
            Choice("updateHealthStaleRelease", "Update health - release older than",
                ThresholdOptions(UpdateHealthDayOptions, DaysLabel)),
            Choice("updateHealthStaleVersion", "Update health - not updated in",
                ThresholdOptions(UpdateHealthDayOptions, DaysLabel)),
            Choice("updateHealthFailedChecks", "Update health - failed checks",
                ThresholdOptions(UpdateHealthFailedChecksOptions, c => c == 1 ? "1 check" : $"{c} checks")),
            Choice("updateHealthArchived", "Update health - archived source", new[]
            {
                new KeyValuePair<string, string>("", "Use global default"),
                new("off", "Disabled"),
                new("on", "Enabled"),
            }),
            Choice("updateHealthFilteredReleases", "Update health - hidden newer releases",
                ThresholdOptions(UpdateHealthFilteredReleasesOptions, r => r == 1 ? "1 release" : $"{r} releases")),
        };

        // ---- Combine exactly like the original ----
        var rows = new List<IReadOnlyList<OptionFormItem>>();
        foreach (var row in source.AdditionalSourceAppSpecificSettingFormItems)
            rows.Add(row);
        if (version.Count > 0)
        {
            rows.Add(new[] { Header("Version detection") });
            foreach (var i in version) rows.Add(new[] { i });
        }
        if (assets.Count > 0)
        {
            rows.Add(new[] { Header("Asset selection") });
            foreach (var i in assets) rows.Add(new[] { i });
        }
        if (update.Count > 0)
        {
            rows.Add(new[] { Header("Update options") });
            foreach (var i in update) rows.Add(new[] { i });
        }
        if (app.Count > 0)
        {
            rows.Add(new[] { Header("App options") });
            foreach (var i in app) rows.Add(new[] { i });
        }
        rows.Add(new[] { Header("Update health detection", "updateHealthSection") });
        foreach (var i in health) rows.Add(new[] { i });
        return rows;
    }
}
