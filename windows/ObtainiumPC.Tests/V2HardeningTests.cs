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
