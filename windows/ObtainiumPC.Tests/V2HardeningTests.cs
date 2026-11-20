using System.IO.Compression;
using ObtainiumPC.Services.Download;
using ObtainiumPC.Services.Versioning;
using ObtainiumPC.Sources;
using Xunit;

namespace ObtainiumPC.Tests;

/// Tests for the 2.0 hardening: portable-zip selection rules, zip extraction limits and
/// the user-regex timeout.
public class V2HardeningTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "obt-v2-" + Guid.NewGuid().ToString("N"));
    public V2HardeningTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Theory]
    [InlineData("tool-1.2.zip", true)]               // generic name: accepted
    [InlineData("tool-1.2-win-x64.zip", true)]
    [InlineData("tool-linux-x64.zip", false)]        // clearly another OS
    [InlineData("tool-macos.zip", false)]
    [InlineData("tool-windows-and-linux.zip", true)] // a Windows marker wins
    [InlineData("tool-symbols.zip", false)]
    [InlineData("Test-Tool-win.zip", true)]          // "test" in a product name is not an auxiliary archive
    [InlineData("tool.exe", false)]
    public void Portable_candidate_rules(string name, bool expected)
        => Assert.Equal(expected, AssetFilterService.IsPortableCandidate(name));

    [Fact]
    public void Release_with_only_a_foreign_zip_is_not_portable()
        => Assert.False(AssetFilterService.IsPortableArchiveRelease(new[] { "tool-macos.zip" }));

    [Fact]
    public void Zip_next_to_an_installer_is_not_extracted_as_an_app()
    {
        Assert.False(AssetFilterService.HasNoInstallerAsset(new[] { "tool-setup.exe", "tool.zip" }));
        Assert.True(AssetFilterService.HasNoInstallerAsset(new[] { "tool.zip", "SHA256SUMS.txt" }));
    }

    [Theory]
    [InlineData("app.exe", true)]
    [InlineData("NUL", false)]
    [InlineData("nul.txt", false)]
    [InlineData("com1.log", false)]
    [InlineData("name.", false)]
    [InlineData("name ", false)]
    [InlineData("..", false)]
    public void Entry_segments_are_checked(string segment, bool safe)
        => Assert.Equal(safe, ZipExtractionService.IsSafeSegment(segment));

    private string MakeZip(string name, params (string Path, string Text)[] entries)
    {
        var path = Path.Combine(_tmp, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, t) in entries)
        {
            var e = z.CreateEntry(p);
            using var w = new StreamWriter(e.Open());
            w.Write(t);
        }
        return path;
    }

    [Fact]
    public void Reserved_device_name_inside_a_zip_is_rejected()
    {
        var zip = MakeZip("r.zip", ("ok.txt", "ok"), ("sub/aux.txt", "x"));
        var root = Path.Combine(_tmp, "Obtainium");
        Assert.Throws<InvalidDataException>(() => ZipExtractionService.Extract(zip, root));
        Assert.Empty(Directory.GetDirectories(root, ".staging-*"));
    }

    [Fact]
    public void Locked_existing_file_fails_before_anything_is_replaced()
    {
        var root = Path.Combine(_tmp, "Obtainium");
        var first = ZipExtractionService.Extract(MakeZip("t.zip", ("a.exe", "v1"), ("b.dll", "v1")), root);
        File.Delete(Path.Combine(_tmp, "t.zip"));
        var zip2 = MakeZip("t.zip", ("a.exe", "v2"), ("b.dll", "v2"));
        using (new FileStream(Path.Combine(first.Folder, "b.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => ZipExtractionService.Extract(zip2, root));
        // Neither file was replaced: the install is not half old, half new.
        Assert.Equal("v1", File.ReadAllText(Path.Combine(first.Folder, "a.exe")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(first.Folder, "b.dll")));
    }

    [Fact]
    public void User_regex_has_a_match_timeout()
    {
        var re = UserRegex.Create("(a+)+$");
        Assert.Equal(UserRegex.MatchTimeout, re.MatchTimeout);
        Assert.ThrowsAny<ArgumentException>(() => UserRegex.Create("("));
    }
}

public class V2ZipSafetyTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "obt-v2b-" + Guid.NewGuid().ToString("N"));
    public V2ZipSafetyTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { ZipExtractionService.FailAfterMovesForTests = null; try { Directory.Delete(_tmp, true); } catch { } }

    private string MakeZip(string name, params (string Path, string Text)[] entries)
    {
        var path = Path.Combine(_tmp, name);
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, t) in entries)
        {
            var e = z.CreateEntry(p);
            using var w = new StreamWriter(e.Open());
            w.Write(t);
        }
        return path;
    }

    [Fact]
    public void Archive_kinds_are_told_apart()
    {
        Assert.Equal(ZipExtractionService.ArchiveKind.PortableApp,
            ZipExtractionService.Classify(MakeZip("a.zip", ("tool.exe", "x"), ("lib/a.dll", "x"))));
        Assert.Equal(ZipExtractionService.ArchiveKind.ContainsInstaller,
            ZipExtractionService.Classify(MakeZip("b.zip", ("tool-setup.exe", "x"))));
        Assert.Equal(ZipExtractionService.ArchiveKind.ContainsInstaller,
            ZipExtractionService.Classify(MakeZip("c.zip", ("tool.msi", "x"), ("readme.txt", "x"))));
        Assert.Equal(ZipExtractionService.ArchiveKind.NoProgram,
            ZipExtractionService.Classify(MakeZip("d.zip", ("docs/readme.md", "x"), ("theme.json", "x"))));
        File.WriteAllText(Path.Combine(_tmp, "bad.zip"), "not a zip");
        Assert.Equal(ZipExtractionService.ArchiveKind.Unreadable, ZipExtractionService.Classify(Path.Combine(_tmp, "bad.zip")));
    }

    [Fact]
    public void Failure_part_way_rolls_back_to_the_old_version_completely()
    {
        var root = Path.Combine(_tmp, "Obtainium");
        var first = ZipExtractionService.Extract(MakeZip("t.zip", ("a.exe", "v1"), ("b.dll", "v1"), ("c.dat", "v1")), root, ownerId: "app1");
        var zip2 = MakeZip("t.zip", ("a.exe", "v2"), ("b.dll", "v2"), ("c.dat", "v2"), ("new.txt", "v2"));
        ZipExtractionService.FailAfterMovesForTests = 2;
        Assert.Throws<IOException>(() => ZipExtractionService.Extract(zip2, root, ownerId: "app1"));
        ZipExtractionService.FailAfterMovesForTests = null;
        Assert.Equal("v1", File.ReadAllText(Path.Combine(first.Folder, "a.exe")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(first.Folder, "b.dll")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(first.Folder, "c.dat")));
        Assert.False(File.Exists(Path.Combine(first.Folder, "new.txt")));
        Assert.Empty(Directory.GetDirectories(root, ".staging-*"));
        Assert.Empty(Directory.GetDirectories(root, ".backup-*"));
    }

    [Fact]
    public void Concurrent_extractions_into_one_target_end_as_one_complete_version()
    {
        var root = Path.Combine(_tmp, "Obtainium");
        var zip1 = MakeZip("t1.zip", ("a.exe", "A"), ("b.dll", "A"));
        var zip2 = Path.Combine(_tmp, "t.zip");
        File.Copy(zip1, zip2, true);
        var zipB = MakeZip("tB.zip", ("a.exe", "B"), ("b.dll", "B"));
        var zipB2 = Path.Combine(_tmp, "t.zip.b");
        // Same target folder name ("t") and owner from two threads, different contents.
        var folderA = Path.Combine(_tmp, "A"); Directory.CreateDirectory(folderA);
        var a = Path.Combine(folderA, "t.zip"); File.Copy(zip1, a, true);
        var folderB = Path.Combine(_tmp, "B"); Directory.CreateDirectory(folderB);
        var b = Path.Combine(folderB, "t.zip"); File.Copy(zipB, b, true);
        var tasks = new[]
        {
            Task.Run(() => { try { ZipExtractionService.Extract(a, root, ownerId: "app1"); } catch (IOException) { } }),
            Task.Run(() => { try { ZipExtractionService.Extract(b, root, ownerId: "app1"); } catch (IOException) { } }),
        };
        Task.WaitAll(tasks);
        var folder = Path.Combine(root, "t");
        var exe = File.ReadAllText(Path.Combine(folder, "a.exe"));
        var dll = File.ReadAllText(Path.Combine(folder, "b.dll"));
        Assert.Equal(exe, dll); // never a mix of the two versions
    }

    [Fact]
    public void Another_apps_folder_is_never_reused()
    {
        var root = Path.Combine(_tmp, "Obtainium");
        var zip = MakeZip("setup-tool.zip", ("tool.exe", "x"));
        var one = ZipExtractionService.Extract(zip, root, ownerId: "appA");
        var two = ZipExtractionService.Extract(zip, root, ownerId: "appB");
        var again = ZipExtractionService.Extract(zip, root, ownerId: "appA");
        Assert.NotEqual(one.Folder, two.Folder);
        Assert.EndsWith("(2)", two.Folder);
        Assert.Equal(one.Folder, again.Folder);
    }
}

public class V2InPlaceUpdateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "obt-v2c-" + Guid.NewGuid().ToString("N"));
    public V2InPlaceUpdateTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { ZipExtractionService.FailAfterMovesForTests = null; try { Directory.Delete(_tmp, true); } catch { } }

    private string MakeZip(string name, params (string Path, string Text)[] entries)
    {
        var path = Path.Combine(_tmp, name);
        if (File.Exists(path)) File.Delete(path);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, t) in entries)
        {
            var e = z.CreateEntry(p);
            using var w = new StreamWriter(e.Open());
            w.Write(t);
        }
        return path;
    }

    private string Root => Path.Combine(_tmp, "Obtainium");

    [Fact]
    public void Folder_is_named_after_the_app_and_a_new_version_replaces_the_old_in_place()
    {
        var one = ZipExtractionService.Extract(MakeZip("Tool-1.0.zip", ("tool.exe", "v1")), Root, ownerId: "a", folderName: "Tool");
        var two = ZipExtractionService.Extract(MakeZip("Tool-2.0.zip", ("tool.exe", "v2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal(one.Folder, two.Folder);
        Assert.EndsWith("Tool", two.Folder);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(two.Folder, "tool.exe")));
        Assert.Single(Directory.GetDirectories(Root)); // no accumulated version folders
    }

    [Fact]
    public void Obsolete_program_files_are_removed_but_user_files_survive()
    {
        var r1 = ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("old.dll", "v1"), ("lib/x.dll", "v1")), Root, ownerId: "a", folderName: "Tool");
        File.WriteAllText(Path.Combine(r1.Folder, "my-notes.md"), "mine");
        Directory.CreateDirectory(Path.Combine(r1.Folder, "userdata"));
        File.WriteAllText(Path.Combine(r1.Folder, "userdata", "save.bin"), "save");
        var r2 = ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("new.dll", "v2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("v2", File.ReadAllText(Path.Combine(r2.Folder, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(r2.Folder, "new.dll")));
        Assert.False(File.Exists(Path.Combine(r2.Folder, "old.dll")));       // stale binary gone
        Assert.False(Directory.Exists(Path.Combine(r2.Folder, "lib")));      // emptied folder gone
        Assert.Equal("mine", File.ReadAllText(Path.Combine(r2.Folder, "my-notes.md")));
        Assert.Equal("save", File.ReadAllText(Path.Combine(r2.Folder, "userdata", "save.bin")));
    }

    [Fact]
    public void A_user_edited_settings_file_is_kept_and_the_new_copy_goes_next_to_it()
    {
        var r1 = ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("config.ini", "default1")), Root, ownerId: "a", folderName: "Tool");
        File.WriteAllText(Path.Combine(r1.Folder, "config.ini"), "edited by user");
        var r2 = ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("config.ini", "default2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("edited by user", File.ReadAllText(Path.Combine(r2.Folder, "config.ini")));
        Assert.Equal("default2", File.ReadAllText(Path.Combine(r2.Folder, "config.ini.new")));
        Assert.Contains("config.ini", r2.KeptUserFiles!);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(r2.Folder, "tool.exe")));
    }

    [Fact]
    public void An_unedited_settings_file_is_simply_replaced()
    {
        var r1 = ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("config.ini", "default1")), Root, ownerId: "a", folderName: "Tool");
        var r2 = ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("config.ini", "default2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("default2", File.ReadAllText(Path.Combine(r2.Folder, "config.ini")));
        Assert.False(File.Exists(Path.Combine(r2.Folder, "config.ini.new")));
    }

    [Fact]
    public void Failure_restores_replaced_and_removed_files()
    {
        var r1 = ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("old.dll", "v1"), ("keep.dll", "v1")), Root, ownerId: "a", folderName: "Tool");
        var zip2 = MakeZip("t2.zip", ("tool.exe", "v2"), ("keep.dll", "v2"), ("new.dll", "v2"));
        ZipExtractionService.FailAfterMovesForTests = 2;
        Assert.Throws<IOException>(() => ZipExtractionService.Extract(zip2, Root, ownerId: "a", folderName: "Tool"));
        ZipExtractionService.FailAfterMovesForTests = null;
        Assert.Equal("v1", File.ReadAllText(Path.Combine(r1.Folder, "tool.exe")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(r1.Folder, "old.dll")));   // removed stale file came back
        Assert.Equal("v1", File.ReadAllText(Path.Combine(r1.Folder, "keep.dll")));
        Assert.False(File.Exists(Path.Combine(r1.Folder, "new.dll")));
        Assert.Empty(Directory.GetDirectories(Root, ".staging-*"));
        Assert.Empty(Directory.GetDirectories(Root, ".backup-*"));
    }

    [Fact]
    public void Different_apps_with_the_same_name_do_not_share_a_folder()
    {
        var one = ZipExtractionService.Extract(MakeZip("a.zip", ("tool.exe", "A")), Root, ownerId: "appA", folderName: "Tool");
        var two = ZipExtractionService.Extract(MakeZip("b.zip", ("tool.exe", "B")), Root, ownerId: "appB", folderName: "Tool");
        Assert.NotEqual(one.Folder, two.Folder);
        Assert.Equal("A", File.ReadAllText(Path.Combine(one.Folder, "tool.exe")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(two.Folder, "tool.exe")));
    }

    [Fact]
    public void Kept_settings_stay_protected_on_the_next_update_and_new_copies_do_not_pile_up()
    {
        ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("config.ini", "d1")), Root, ownerId: "a", folderName: "Tool");
        var f = Path.Combine(Root, "Tool");
        File.WriteAllText(Path.Combine(f, "config.ini"), "mine");
        ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("config.ini", "d2")), Root, ownerId: "a", folderName: "Tool");
        ZipExtractionService.Extract(MakeZip("t3.zip", ("tool.exe", "v3"), ("config.ini", "d3")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("mine", File.ReadAllText(Path.Combine(f, "config.ini")));   // never mistaken for an untouched default
        Assert.Equal("d3", File.ReadAllText(Path.Combine(f, "config.ini.new")));  // old .new replaced, not duplicated
        Assert.False(File.Exists(Path.Combine(f, "config.ini.new2")));
    }

    [Fact]
    public void A_file_the_user_created_with_a_new_name_is_never_overwritten()
    {
        ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("config.ini", "d1")), Root, ownerId: "a", folderName: "Tool");
        var f = Path.Combine(Root, "Tool");
        File.WriteAllText(Path.Combine(f, "config.ini"), "mine");
        File.WriteAllText(Path.Combine(f, "config.ini.new"), "user's own file");
        ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("config.ini", "d2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("user's own file", File.ReadAllText(Path.Combine(f, "config.ini.new")));
        Assert.Equal("d2", File.ReadAllText(Path.Combine(f, "config.ini.new2")));
    }

    [Fact]
    public void A_user_modified_library_is_replaced_but_the_original_is_saved()
    {
        ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("a.dll", "d1")), Root, ownerId: "a", folderName: "Tool");
        var f = Path.Combine(Root, "Tool");
        File.WriteAllText(Path.Combine(f, "a.dll"), "patched by user");
        var r = ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2"), ("a.dll", "d2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("d2", File.ReadAllText(Path.Combine(f, "a.dll")));
        Assert.Equal("patched by user", File.ReadAllText(Path.Combine(f, "a.dll.user-backup")));
        Assert.Single(r.KeptUserFiles!);
    }

    [Fact]
    public void A_user_modified_file_the_new_version_dropped_is_left_alone()
    {
        ZipExtractionService.Extract(MakeZip("t1.zip", ("tool.exe", "v1"), ("old.dll", "d1")), Root, ownerId: "a", folderName: "Tool");
        var f = Path.Combine(Root, "Tool");
        File.WriteAllText(Path.Combine(f, "old.dll"), "changed");
        ZipExtractionService.Extract(MakeZip("t2.zip", ("tool.exe", "v2")), Root, ownerId: "a", folderName: "Tool");
        Assert.Equal("changed", File.ReadAllText(Path.Combine(f, "old.dll")));
    }
}

public class V2ChecksumTests
{
    private static readonly string H = new string('a', 64);

    [Fact]
    public void A_bare_hash_file_is_used_only_when_named_after_the_file()
    {
        Assert.Equal(H, ObtainiumPC.Services.Download.DownloadService.ExpectedSha256(H + "\n", "app.zip", "app.zip.sha256"));
        Assert.Null(ObtainiumPC.Services.Download.DownloadService.ExpectedSha256(H + "\n", "app.zip", "other.zip.sha256"));
    }
}

public class V2SmallFixTests
{
    [Theory]
    [InlineData("..", "download.bin")]
    [InlineData(".", "download.bin")]
    [InlineData("setup.exe.", "setup.exe")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("a/b:c.exe", "a_b_c.exe")]
    [InlineData("fine-1.2.exe", "fine-1.2.exe")]
    public void Download_file_names_cannot_escape_or_name_devices(string input, string expected)
        => Assert.Equal(expected, ObtainiumPC.Services.Download.DownloadService.Sanitize(input));

    [Fact]
    public void Very_long_numeric_runs_compare_without_throwing()
    {
        var a = "1.20260101123456789012345.7";
        var b = "1.20260101123456789012346.7";
        var r = ObtainiumPC.Services.Versioning.VersionService.CompareVersionsNumerically(a, b);
        Assert.True(r is null or -1);
    }

    [Theory]
    [InlineData("https://proxy.example/", "proxy.example")]
    [InlineData("http://proxy.example/x//", "proxy.example/x")]
    [InlineData("  ", null)]
    [InlineData("proxy.example", "proxy.example")]
    public void Proxy_prefix_is_stored_without_scheme_or_trailing_slash(string input, string? expected)
        => Assert.Equal(expected, ObtainiumPC.Services.Settings.AppSettings.CleanPrefix(input));
}

public class V2TokenHostTests
{
    [Theory]
    [InlineData("https://github.com/a/b/releases/download/v1/x.zip", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://api.github.com/repos/a/b", true)]
    [InlineData("https://github.com.evil.com/x", false)]
    [InlineData("https://evil.com/?x=github.com", false)]
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("ftp://github.com/x", false)]
    [InlineData("not a url", false)]
    public void The_token_goes_only_to_real_GitHub_hosts(string url, bool expected)
        => Assert.Equal(expected, ObtainiumPC.Services.Download.DownloadService.IsGitHubHost(url));
}

