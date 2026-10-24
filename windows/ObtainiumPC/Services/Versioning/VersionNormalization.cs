using System.Text.RegularExpressions;

namespace ObtainiumPC.Services.Versioning;

/// <summary>
/// Port of lib/utils/version_normalization.dart: recognizes when two strings
/// describe the same release despite cosmetic packaging differences, without
/// ever hiding a real update.
/// </summary>
public static class VersionNormalization
{
    private static readonly HashSet<string> IgnorableVersionTokens = new()
    {
        "strip", "debug", "release", "stable", "final", "standard",
        "build", "signed", "unsigned", "universal", "nogms",
    };

    private static readonly string[] PreReleaseQualifierList =
        { "alpha", "beta", "rc", "pre", "dev", "snapshot", "nightly", "ose" };

    private static readonly Regex LeadingV = new(@"^v(?=\d)", RegexOptions.Compiled);
    private static readonly Regex TokenSeparator = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex Segments = new(@"\p{L}+|\p{N}+", RegexOptions.Compiled);
    private static readonly Regex FirstNumber = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex MajorOnlyPreRelease = new(
        @"^(\d+)(?:[._\-\s]?(" + string.Join("|", PreReleaseQualifierList) + @")\w*)$",
        RegexOptions.Compiled);

    private sealed class VersionParts
    {
        public List<string> Core = new();
        public HashSet<string> Packaging = new();
        public List<string> Metadata = new();
    }

    private static bool SameTokens(List<string> a, List<string> b) => a.SequenceEqual(b);

    private static List<string> Segment(string token) =>
        Segments.Matches(token).Select(m => m.Value).ToList();

    private static List<string> Tokens(string value) =>
        TokenSeparator.Split(value).Where(t => t.Length > 0).ToList();

    private static VersionParts Analyze(string version)
    {
        var value = version.Trim().ToLowerInvariant().Replace('_', '-');
        value = LeadingV.Replace(value, "", 1);
        var plus = value.IndexOf('+');
        var versionPart = plus >= 0 ? value.Substring(0, plus) : value;
        var metadataPart = plus >= 0 ? value.Substring(plus + 1) : "";
        var parts = new VersionParts();
        foreach (var token in Tokens(versionPart))
        {
            if (IgnorableVersionTokens.Contains(token)) parts.Packaging.Add(token);
            else parts.Core.AddRange(Segment(token));
        }
        foreach (var token in Tokens(metadataPart))
        {
            if (IgnorableVersionTokens.Contains(token)) continue;
            parts.Metadata.AddRange(Segment(token));
        }
        return parts;
    }

    /// Canonical key keeping the numeric core and ordering-significant words.
    public static string NormalizeVersionForComparison(string version) =>
        string.Join(".", Analyze(version).Core);

    /// Same underlying release once cosmetic packaging words are ignored.
    /// Symmetric; use InstalledMatchesRemote for the update decision.
    public static bool VersionsAreCosmeticallyEqual(string a, string b)
    {
        var pa = Analyze(a);
        var pb = Analyze(b);
        if (pa.Core.Count == 0 || pb.Core.Count == 0) return false;
        if (!SameTokens(pa.Core, pb.Core)) return false;
        if (pa.Metadata.Count > 0 && pb.Metadata.Count > 0 && !SameTokens(pa.Metadata, pb.Metadata)) return false;
        return true;
    }

    /// Whether the installed build is the same release as the published one,
    /// allowing the installed side to carry extra packaging words or metadata.
    public static bool InstalledMatchesRemote(string installed, string remote)
    {
        var pi = Analyze(installed);
        var pr = Analyze(remote);
        if (pi.Core.Count == 0 || pr.Core.Count == 0) return false;
        if (!SameTokens(pi.Core, pr.Core)) return false;
        if (!pr.Packaging.IsSubsetOf(pi.Packaging)) return false;
        if (pr.Metadata.Count > 0 && !SameTokens(pi.Metadata, pr.Metadata)) return false;
        return true;
    }

    /// Whether tag is a major-only pre-release tag (e.g. v151_beta) for the
    /// same major version as installed (e.g. 151.0.7922.47).
    public static bool IsPreReleaseMajorMatch(string tag, string installed)
    {
        var normalizedTag = tag.Trim().ToLowerInvariant().Replace('_', '-');
        normalizedTag = LeadingV.Replace(normalizedTag, "", 1);
        var match = MajorOnlyPreRelease.Match(normalizedTag);
        if (!match.Success) return false;
        var installedCore = Analyze(installed).Core;
        if (installedCore.Count == 0) return false;
        var installedMajor = FirstNumber.Match(string.Join(".", installedCore));
        return installedMajor.Success && installedMajor.Value == match.Groups[1].Value;
    }
}
