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
        Assert.Throws<ArgumentException>(() => UserRegex.Create("("));
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

public class V2FolderNameTests
{
    [Fact]
    public void Unversioned_zip_name_gets_the_release_version()
    {
        var root = Path.Combine(Path.GetTempPath(), "obt-fn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(Path.Combine(root, "tool v1.2"), ZipExtractionService.ChooseTarget(root, "tool.zip", "a", "v1.2"));
            Assert.Equal(Path.Combine(root, "tool-1.2"), ZipExtractionService.ChooseTarget(root, "tool-1.2.zip", "a", "v1.2"));
            Assert.Equal(Path.Combine(root, "tool"), ZipExtractionService.ChooseTarget(root, "tool.zip", "a", null));
        }
        finally { Directory.Delete(root, true); }
    }
}
