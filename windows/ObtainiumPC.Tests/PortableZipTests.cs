using System.IO.Compression;
using System.Text.Json;
using ObtainiumPC.Services.Download;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.GitHub;
using Xunit;

namespace ObtainiumPC.Tests;

/// Portable apps: repos whose release is only a .zip are accepted, and the zip is extracted
/// to "<root>\Obtainium\<zip name>\" instead of being launched.
[Collection("ZipFaultInjection")]
public class PortableZipTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "obt-zip-" + Guid.NewGuid().ToString("N"));
    public PortableZipTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    // ---- asset selection ----------------------------------------------------------------

    private const string RepoUrl = "https://github.com/owner/tool";
    private const string ReleasesUrl = "https://api.github.com/repos/owner/tool/releases?per_page=100";

    private static GitHubSource Source(string releasesJson) => new(
        new GitHubSourceConfig(CheckRepoRename: false, UpdateHealthArchivedEnabled: false),
        FixtureHttpHandler.Client(new FixtureHttpHandler().Respond(ReleasesUrl, releasesJson)));

    private static readonly IReadOnlyDictionary<string, JsonElement> NoSettings = new Dictionary<string, JsonElement>();

    private static string Release(params string[] assetNames)
    {
        var assets = string.Join(",", assetNames.Select((n, i) =>
            $"{{ \"name\": \"{n}\", \"url\": \"https://api.github.com/repos/owner/tool/releases/assets/{i}\"," +
            $" \"browser_download_url\": \"https://github.com/owner/tool/releases/download/v1.0/{n}\" }}"));
        return "[{ \"tag_name\": \"v1.0\", \"name\": \"v1.0\", \"prerelease\": false, \"draft\": false," +
               " \"published_at\": \"2026-09-01T00:00:00Z\", \"body\": \"\"," +
               " \"tarball_url\": \"https://api.github.com/repos/owner/tool/tarball/v1.0\"," +
               " \"zipball_url\": \"https://api.github.com/repos/owner/tool/zipball/v1.0\"," +
               " \"assets\": [" + assets + "] }]";
    }

    [Fact]
    public async Task Zip_only_release_is_accepted_without_the_include_zips_option()
    {
        var details = await Source(Release("tool-1.0-win-x64.zip")).GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("tool-1.0-win-x64.zip", asset.Key);
    }

    [Fact]
    public async Task Zip_only_release_skips_symbols_and_source_archives()
    {
        var details = await Source(Release("tool-1.0-win-x64.zip", "tool-1.0-symbols.zip", "tool-1.0-src.zip"))
            .GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("tool-1.0-win-x64.zip", asset.Key);
    }

    [Fact]
    public async Task Release_with_an_installer_still_ignores_zips_unless_opted_in()
    {
        var details = await Source(Release("tool-1.0-setup.exe", "tool-1.0-win-x64.zip"))
            .GetLatestAssetDetailsAsync(RepoUrl, NoSettings);
        var asset = Assert.Single(details.AssetUrls);
        Assert.Equal("tool-1.0-setup.exe", asset.Key);
    }

    [Fact]
    public async Task Android_package_next_to_a_zip_is_not_treated_as_a_portable_windows_app()
    {
        await Assert.ThrowsAsync<NoAppAssetException>(() =>
            Source(Release("tool-1.0.apk", "tool-1.0-extras.zip")).GetLatestAssetDetailsAsync(RepoUrl, NoSettings));
    }

    [Fact]
    public void Windows_zip_is_preferred_over_other_platforms()
    {
        var assets = new List<KeyValuePair<string, string>>
        {
            new("tool-linux-x64.zip", "u1"), new("tool-macos.zip", "u2"), new("tool-win-x64.zip", "u3"), new("tool-win-arm64.zip", "u4"),
        };
        var picked = AssetFilterService.PreferWindowsArchives(assets, new[] { "x64" });
        Assert.Equal("tool-win-x64.zip", Assert.Single(picked).Key);
    }

    [Fact]
    public void Msvc_build_is_the_default_among_same_cpu_windows_zips_in_a_mixed_release()
    {
        // Modelled on a real release: Windows zips for several toolchains and CPUs next to .deb and .tar.gz files.
        var names = new[]
        {
            "fd_10.5.2_amd64.deb", "fd-v10.5.2-aarch64-apple-darwin.tar.gz", "fd-v10.5.2-aarch64-pc-windows-msvc.zip",
            "fd-v10.5.2-x86_64-pc-windows-gnu.zip", "fd-v10.5.2-x86_64-pc-windows-msvc.zip", "fd-v10.5.2-x86_64-unknown-linux-musl.tar.gz",
        };
        Assert.True(AssetFilterService.IsPortableArchiveRelease(names));
        var assets = names.Select(n => new KeyValuePair<string, string>(n, "u-" + n)).ToList();
        var picked = AssetFilterService.PreferWindowsArchives(assets, new[] { "x64" });
        Assert.Equal("fd-v10.5.2-x86_64-pc-windows-msvc.zip", picked[0].Key);
        Assert.DoesNotContain(picked, a => a.Key.EndsWith(".deb") || a.Key.Contains("linux"));
    }

    [Fact]
    public void Darwin_is_not_mistaken_for_windows()
    {
        var assets = new List<KeyValuePair<string, string>> { new("tool-darwin-x64.zip", "u1"), new("tool-linux-x64.zip", "u2") };
        // No Windows marker at all: other-OS zips are dropped, and if that empties the pool the input is kept.
        Assert.Equal(2, AssetFilterService.PreferWindowsArchives(assets, new[] { "x64" }).Count);
    }

    [Theory]
    [InlineData("tool-1.0.zip", true)]
    [InlineData("TOOL.ZIP", true)]
    [InlineData("tool.exe", false)]
    public void IsZipArchive_matches_the_extension(string name, bool expected)
        => Assert.Equal(expected, AssetFilterService.IsZipArchive(name));

    [Fact]
    public void Planner_extracts_zips_and_not_installers()
    {
        Assert.True(BatchDownloadPlanner.ShouldExtractArchive(null, "tool.zip"));
        Assert.True(BatchDownloadPlanner.ShouldExtractArchive(true, "tool.zip"));
        Assert.False(BatchDownloadPlanner.ShouldExtractArchive(false, "tool.zip"));
        Assert.False(BatchDownloadPlanner.ShouldExtractArchive(true, "tool.exe"));
        Assert.False(BatchDownloadPlanner.ShouldLaunchInstaller(true, "tool.zip"));
    }

    // ---- extraction ---------------------------------------------------------------------

    private string MakeZip(string name, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_tmp, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(content);
        }
        return path;
    }

    [Theory]
    [InlineData(@"C:\dl\tool-1.2.3-win-x64.zip", "tool-1.2.3-win-x64")]
    [InlineData(@"C:\dl\CON.zip", "_CON")]
    [InlineData(@"C:\dl\name with spaces.zip", "name with spaces")]
    public void Folder_is_named_after_the_zip(string zipPath, string expected)
        => Assert.Equal(expected, ZipExtractionService.FolderNameFor(zipPath));

    [Fact]
    public void Extracts_into_a_folder_named_after_the_zip()
    {
        var zip = MakeZip("tool-1.0.zip", ("tool.exe", "exe"), ("data/readme.txt", "hi"));
        var root = Path.Combine(_tmp, "Obtainium");
        var result = ZipExtractionService.Extract(zip, root);
        Assert.Equal(Path.Combine(root, "tool-1.0"), result.Folder);
        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(result.Folder, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(result.Folder, "data", "readme.txt")));
        Assert.Empty(Directory.GetDirectories(root, ".staging-*"));
    }

    [Fact]
    public void A_single_top_level_folder_is_flattened()
    {
        var zip = MakeZip("tool-1.0.zip", ("tool-1.0/tool.exe", "exe"), ("tool-1.0/lib/a.dll", "dll"));
        var result = ZipExtractionService.Extract(zip, Path.Combine(_tmp, "Obtainium"));
        Assert.True(File.Exists(Path.Combine(result.Folder, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(result.Folder, "lib", "a.dll")));
        Assert.False(Directory.Exists(Path.Combine(result.Folder, "tool-1.0")));
    }

    [Fact]
    public void Re_extracting_overwrites_files_and_keeps_user_data()
    {
        var root = Path.Combine(_tmp, "Obtainium");
        var zip = MakeZip("tool.zip", ("tool.exe", "v1"));
        var first = ZipExtractionService.Extract(zip, root);
        File.WriteAllText(Path.Combine(first.Folder, "settings.ini"), "mine");
        File.Delete(zip);
        zip = MakeZip("tool.zip", ("tool.exe", "v2"));
        ZipExtractionService.Extract(zip, root);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(first.Folder, "tool.exe")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(first.Folder, "settings.ini")));
    }

    [Fact]
    public void Zip_slip_entries_are_rejected_and_nothing_is_left_behind()
    {
        var zip = MakeZip("evil.zip", ("ok.txt", "ok"), ("../evil.txt", "x"));
        var root = Path.Combine(_tmp, "Obtainium");
        Assert.Throws<InvalidDataException>(() => ZipExtractionService.Extract(zip, root));
        Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_tmp, "evil.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, "evil")));
    }

    [Fact]
    public void Empty_zip_is_an_error()
    {
        var path = Path.Combine(_tmp, "empty.zip");
        using (ZipFile.Open(path, ZipArchiveMode.Create)) { }
        Assert.Throws<InvalidDataException>(() => ZipExtractionService.Extract(path, Path.Combine(_tmp, "Obtainium")));
    }
}
