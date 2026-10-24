using System.Text.RegularExpressions;

namespace ObtainiumPC.Services.Versioning;

/// Thrown when a configured version-extraction regex yields nothing.
public sealed class NoVersionException : Exception { }

/// Result of reconciling two version strings under a common standard format.
public readonly record struct VersionComparison(bool AreEqual, string Version);

/// <summary>
/// Port of lib/services/version_service.dart: version extraction, standard-format
/// classification, comparison, and install-status reconciliation. Pure, no I/O.
/// </summary>
public static class VersionService
{
    private const string DefaultMatchGroup = "0";

    // =====================================================================
    // Extraction
    // =====================================================================

    /// Returns an error key when value is not a valid regular expression.
    public static string? RegExValidator(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { _ = new Regex(value); return null; }
        catch { return "invalidRegEx"; }
    }

    /// Applies versionExtractionRegEx and returns the captured match group
    /// (whole match when matchGroupString is empty). Returns null when no
    /// extraction regex is configured; throws NoVersionException on no match.
    public static string? ExtractVersion(string? versionExtractionRegEx, string? matchGroupString, string stringToCheck)
    {
        if (string.IsNullOrEmpty(versionExtractionRegEx)) return null;
        var matches = new Regex(versionExtractionRegEx).Matches(stringToCheck);
        if (matches.Count == 0) throw new NoVersionException();
        var trimmedGroup = matchGroupString?.Trim();
        var version = ReplaceMatchGroupsInString(
            matches[matches.Count - 1],
            string.IsNullOrEmpty(trimmedGroup) ? DefaultMatchGroup : trimmedGroup!);
        if (string.IsNullOrEmpty(version)) throw new NoVersionException();
        return version;
    }

    /// Replaces $N references with the corresponding regex match groups.
    /// A backslash before a reference keeps it literal.
    private static string? ReplaceMatchGroupsInString(Match match, string matchGroupString)
    {
        if (Regex.IsMatch(matchGroupString, @"^\d+$")) matchGroupString = "$" + matchGroupString;
        var numberRegex = new Regex(@"\$\d+");
        var numbers = numberRegex.Matches(matchGroupString);
        if (numbers.Count == 0) return null;
        var outputString = matchGroupString;
        foreach (Match numberMatch in numbers)
        {
            var number = numberMatch.Value;
            var groupIndex = int.Parse(number.Substring(1));
            var matchGroup = groupIndex < match.Groups.Count ? match.Groups[groupIndex].Value : "";
            var isEscaped = outputString.Contains("\\" + number);
            if (!isEscaped) outputString = outputString.Replace(number, matchGroup);
            else outputString = outputString.Replace("\\" + number, number);
        }
        return outputString;
    }

    // =====================================================================
    // Standard version formats
    // =====================================================================

    private static readonly List<string> StandardVersionPatterns = GenerateStandardVersionPatterns();

    private static readonly List<KeyValuePair<string, Regex>> StrictStandardVersionRegExes =
        StandardVersionPatterns.Select(p => new KeyValuePair<string, Regex>(p, new Regex("^" + p + "$", RegexOptions.Compiled))).ToList();

    private static readonly List<KeyValuePair<string, Regex>> LooseStandardVersionRegExes =
        StandardVersionPatterns.Select(p => new KeyValuePair<string, Regex>(p, new Regex(p, RegexOptions.Compiled))).ToList();

    private static List<string> GenerateStandardVersionPatterns()
    {
        var basics = new[]
        {
            "[0-9]+",
            "[0-9]+\\.[0-9]+",
            "[0-9]+\\.[0-9]+\\.[0-9]+",
            "[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+",
        };
        var preSuffixes = new[] { "-", "\\+" };
        var suffixes = new[] { "alpha", "beta", "rc", "pre", "dev", "snapshot", "nightly", "ose", "[0-9]+" };
        var finals = new[] { "\\+[0-9]+", "[0-9]+" };
        var results = new List<string>();
        var seen = new HashSet<string>();
        void Add(string p) { if (seen.Add(p)) results.Add(p); }
        foreach (var basic in basics)
        {
            Add(basic);
            foreach (var preSuffix in preSuffixes)
            foreach (var suffix in suffixes)
            {
                Add(basic + suffix);
                Add(basic + preSuffix + suffix);
                foreach (var finalSuffix in finals)
                {
                    Add(basic + suffix + finalSuffix);
                    Add(basic + preSuffix + suffix + finalSuffix);
                }
            }
        }
        return results;
    }

    private static readonly Dictionary<string, HashSet<string>> StrictFormatCache = new();
    private static readonly Dictionary<string, HashSet<string>> LooseFormatCache = new();
    private const int MaxFormatCacheSize = 4096;

    /// The set of standard format patterns that version matches. Strict
    /// patterns must match the whole string; loose may match any substring.
    public static HashSet<string> FindStandardFormatsForVersion(string version, bool strict)
    {
        var cache = strict ? StrictFormatCache : LooseFormatCache;
        lock (cache)
        {
            if (cache.TryGetValue(version, out var cached)) return cached;
            var patterns = strict ? StrictStandardVersionRegExes : LooseStandardVersionRegExes;
            var results = new HashSet<string>();
            foreach (var entry in patterns)
                if (entry.Value.IsMatch(version)) results.Add(entry.Key);
            if (cache.Count >= MaxFormatCacheSize) cache.Clear();
            cache[version] = results;
            return results;
        }
    }

    /// Whether the first match of pattern is identical in both strings.
    public static bool DoStringsMatchUnderRegEx(string pattern, string value1, string value2)
    {
        var regExp = new Regex(pattern);
        var match1 = regExp.Match(value1);
        var match2 = regExp.Match(value2);
        return match1.Success && match2.Success && match1.Value == match2.Value;
    }

    /// Compares two versions numerically when they share a common non-strict
    /// standard format. Negative: version1 older; positive: newer; 0: equal;
    /// null: not comparable.
    public static int? CompareVersionsNumerically(string version1, string version2)
    {
        var formats1 = FindStandardFormatsForVersion(version1, strict: false);
        var formats2 = FindStandardFormatsForVersion(version2, strict: false);
        var commonFormats = StandardVersionPatterns.Where(p => formats1.Contains(p) && formats2.Contains(p)).ToList();
        if (commonFormats.Count == 0) return null;
        var digitRunRegex = new Regex("[0-9]+");
        var mostSpecific = commonFormats[0];
        var mostSpecificRuns = digitRunRegex.Matches(mostSpecific).Count;
        foreach (var format in commonFormats)
        {
            var runs = digitRunRegex.Matches(format).Count;
            if (runs > mostSpecificRuns || (runs == mostSpecificRuns && format.Length > mostSpecific.Length))
            {
                mostSpecific = format;
                mostSpecificRuns = runs;
            }
        }
        var mostSpecificRegExp = new Regex(mostSpecific);
        List<long> ExtractNumericRuns(string version) =>
            digitRunRegex.Matches(mostSpecificRegExp.Match(version).Value)
                .Select(m => long.Parse(m.Value)).ToList();
        var runs1 = ExtractNumericRuns(version1);
        var runs2 = ExtractNumericRuns(version2);
        for (var i = 0; i < runs1.Count; i++)
        {
            if (runs1[i] != runs2[i]) return runs1[i] > runs2[i] ? 1 : -1;
        }
        return 0;
    }

    // =====================================================================
    // Reconciliation
    // =====================================================================

    /// Reconciles two version strings that share a standard version format.
    /// Returns null when no common format exists.
    public static VersionComparison? ReconcileVersionDifferences(string templateVersion, string comparisonVersion)
    {
        var templateFormats = FindStandardFormatsForVersion(templateVersion, strict: true);
        var comparisonFormats = FindStandardFormatsForVersion(comparisonVersion, strict: true);
        if (comparisonFormats.Count == 0)
            comparisonFormats = FindStandardFormatsForVersion(comparisonVersion, strict: false);
        var commonFormats = StandardVersionPatterns
            .Where(p => templateFormats.Contains(p) && comparisonFormats.Contains(p)).ToList();
        if (commonFormats.Count == 0) return null;
        foreach (var pattern in commonFormats)
        {
            if (DoStringsMatchUnderRegEx(pattern, comparisonVersion, templateVersion))
                return new VersionComparison(true, comparisonVersion);
        }
        return new VersionComparison(false, templateVersion);
    }

    /// Whether standard version detection can work for an app with the given
    /// on-device and tracked versions.
    public static bool VersionDetectionPossible(
        bool trackOnly,
        bool releaseDateAsVersion,
        bool isHtmlWithNoVersionDetection,
        bool versionDetectionDisallowed,
        string? realInstalledVersion,
        string? trackedVersion,
        string latestVersion,
        bool naiveStandardVersionDetection)
    {
        if (trackOnly || releaseDateAsVersion || isHtmlWithNoVersionDetection || versionDetectionDisallowed)
            return false;
        if (realInstalledVersion == null || trackedVersion == null) return false;
        var rollingTag =
            VersionNormalization.IsPreReleaseMajorMatch(latestVersion, realInstalledVersion) ||
            VersionNormalization.IsPreReleaseMajorMatch(latestVersion, trackedVersion);
        if (rollingTag && !naiveStandardVersionDetection) return false;
        return ReconcileVersionDifferences(realInstalledVersion, trackedVersion) != null
            || VersionNormalization.VersionsAreCosmeticallyEqual(realInstalledVersion, trackedVersion)
            || VersionNormalization.VersionsAreCosmeticallyEqual(realInstalledVersion, latestVersion)
            || naiveStandardVersionDetection;
    }

    /// Applies the string-only install-status reconciliation steps. Returns the
    /// corrected tracked version, or null when nothing changes.
    public static string? ReconcileTrackedVersion(
        string? trackedVersion,
        string? realInstalledVersion,
        string latestVersion,
        bool versionDetectionIsStandard,
        bool naiveStandardVersionDetection)
    {
        if (trackedVersion == null) return null;
        var current = trackedVersion;
        var changed = false;

        bool SameRelease(string version) =>
            VersionNormalization.InstalledMatchesRemote(version, latestVersion);

        void CollapseIfSameRelease(string version)
        {
            if (current == latestVersion) return;
            if (SameRelease(version)) { current = latestVersion; changed = true; }
        }

        CollapseIfSameRelease(current);
        if (realInstalledVersion != null) CollapseIfSameRelease(realInstalledVersion);
        if (realInstalledVersion != null && current != realInstalledVersion && versionDetectionIsStandard)
        {
            var corrected = ReconcileVersionDifferences(realInstalledVersion, current);
            if (corrected is { } c && !c.AreEqual) { current = c.Version; changed = true; }
            else if (naiveStandardVersionDetection &&
                     !VersionNormalization.VersionsAreCosmeticallyEqual(realInstalledVersion, current))
            {
                current = realInstalledVersion;
                changed = true;
            }
        }
        CollapseIfSameRelease(current);
        return changed ? current : null;
    }

    /// Whether an app should be presented as having an update available.
    public static bool IsAppUpdateable(string? installedVersion, string? latestVersion, bool hideDowngrades)
    {
        var installed = installedVersion;
        var latest = latestVersion;
        if (installed == null || installed == latest) return false;
        if (!hideDowngrades) return true;
        var comparison = CompareVersionsNumerically(installed, latest!);
        return comparison == null || comparison <= 0;
    }
}
