using ObtainiumPC.Sources;
using ObtainiumPC.Sources.Codeberg;
using Xunit;

namespace ObtainiumPC.Tests;

// Port of test/codeberg_search_test.dart (searchUriFor group).
public class CodebergSearchUriTests
{
    private static string Origin(Uri u) => u.GetLeftPart(UriPartial.Authority);

    [Fact]
    public void DefaultsToCodeberg()
    {
        Assert.Equal("https://codeberg.org", Origin(CodebergSource.SearchUriFor(null)));
        Assert.Equal("https://codeberg.org", Origin(CodebergSource.SearchUriFor("")));
        Assert.Equal("https://codeberg.org", Origin(CodebergSource.SearchUriFor("   ")));
    }

    [Fact]
    public void AcceptsSchemeLessHosts() =>
        Assert.Equal("https://git.example.com", Origin(CodebergSource.SearchUriFor("git.example.com")));

    [Fact]
    public void KeepsSchemesAndPortsAndStripsPaths()
    {
        Assert.Equal("https://git.example.com", Origin(CodebergSource.SearchUriFor("https://git.example.com/user/repo")));
        Assert.Equal("http://localhost:3000", Origin(CodebergSource.SearchUriFor("http://localhost:3000/user/repo")));
    }

    [Fact]
    public void HonorsFallbackHost() =>
        Assert.Equal("https://forgejo.example", Origin(CodebergSource.SearchUriFor(null, "forgejo.example")));

    [Fact]
    public void RejectsUnusableInput() =>
        Assert.Throws<InvalidUrlException>(() => CodebergSource.SearchUriFor("https://"));
}
