using System.Text.RegularExpressions;

namespace ObtainiumPC.Sources;

/// <summary>
/// Port of lib/services/apk_filter_service.dart with Windows container types:
/// .exe/.msi/.msix(xbundle)/.appx(xbundle)/.appinstaller instead of APK formats,
/// and Windows CPU architectures instead of Android ABIs. .zip and tarballs
/// stay opt-in via the include-zips / include-tarballs options, as upstream.
/// </summary>
public static class AssetFilterService
{
    public static readonly IReadOnlyList<string> AppContainerExtensions = new[]
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller",
    };

    /// APK container extensions from upstream lib/services/apk_filter_service.dart -
    /// used by track-only Android sources whose assets are APK packages.
    public static readonly IReadOnlyList<string> ApkContainerExtensions = new[]
    {
        ".apk", ".xapk", ".apkm", ".apks",
    };

    /// Port of upstream ApkFilterService.isApkOrContainerFile (Android assets).
    public static bool IsApkOrContainerFile(string name, bool includeArchives = false, bool includeTarballs = false)
    {
        var lower = name.ToLowerInvariant();
        bool EndsWithAny(IReadOnlyList<string> exts)
        {
            foreach (var e in exts) if (lower.EndsWith(e)) return true;
            return false;
        }
        return EndsWithAny(ApkContainerExtensions)
            || (includeArchives && EndsWithAny(ArchiveExtensions))
            || (includeTarballs && EndsWithAny(TarballExtensions));
    }

    public static readonly IReadOnlyList<string> ArchiveExtensions = new[] { ".zip" };

    public static readonly IReadOnlyList<string> TarballExtensions = new[]
    {
        ".tar.gz", ".tgz", ".tar.bz2", ".tar.xz",
    };

    public static bool IsAppOrContainerFile(string name, bool includeArchives = false, bool includeTarballs = false)
    {
        var lower = name.ToLowerInvariant();
        bool EndsWithAny(IReadOnlyList<string> exts)
        {
            foreach (var e in exts) if (lower.EndsWith(e)) return true;
            return false;
        }
        return EndsWithAny(AppContainerExtensions)
            || (includeArchives && EndsWithAny(ArchiveExtensions))
            || (includeTarballs && EndsWithAny(TarballExtensions));
    }

    /// Non-canonical arch names commonly used in Windows installer filenames.
    public static readonly IReadOnlyDictionary<string, string[]> ArchNameAliases =
        new Dictionary<string, string[]>
        {
            ["x64"] = new[] { "x86_64", "amd64", "win64", "64bit", "x86-64" },
            ["arm64"] = new[] { "aarch64" },
            ["x86"] = new[] { "i386", "i686", "win32", "32bit", "x32" },
        };

    public static List<KeyValuePair<string, string>> FilterAssets(
        List<KeyValuePair<string, string>> assetUrls, string? assetFilterRegEx, bool? invert)
    {
        if (!string.IsNullOrEmpty(assetFilterRegEx))
        {
            var reg = new Regex(assetFilterRegEx);
            assetUrls = assetUrls.Where(e => invert == true ? !reg.IsMatch(e.Key) : reg.IsMatch(e.Key)).ToList();
        }
        return assetUrls;
    }

    /// Keeps assets matching the device architectures (in priority order),
    /// with universal builds preserved as an alternative, as upstream does.
    public static List<KeyValuePair<string, string>> FilterAssetsByArch(
        List<KeyValuePair<string, string>> assetUrls, IReadOnlyList<string> arches)
    {
        if (assetUrls.Count > 1)
        {
            foreach (var arch in arches)
            {
                var variants = new List<string> { arch };
                if (ArchNameAliases.TryGetValue(arch, out var aliases)) variants.AddRange(aliases);
                var archRegex = new Regex(".*(?:" + string.Join("|", variants) + ").*", RegexOptions.IgnoreCase);
                var urls2 = assetUrls.Where(e => archRegex.IsMatch(e.Key)).ToList();
                if (urls2.Count > 0 && urls2.Count < assetUrls.Count)
                {
                    var universal = assetUrls.Where(e =>
                        e.Key.ToLowerInvariant().Contains("universal") &&
                        !urls2.Any(m => m.Value == e.Value));
                    assetUrls = universal.Concat(urls2).ToList();
                    break;
                }
            }
        }
        return assetUrls;
    }

    public static List<KeyValuePair<string, string>> GetAssetUrlsFromUrls(IReadOnlyList<string> urls) =>
        urls.Select(e =>
        {
            var segments = e.Split('/').Where(el => el.Trim().Length > 0).ToList();
            var assetSegs = segments.Where(s => IsAppOrContainerFile(s)).ToList();
            return new KeyValuePair<string, string>(assetSegs.Count > 0 ? assetSegs[assetSegs.Count - 1] : segments[segments.Count - 1], e);
        }).ToList();
}
