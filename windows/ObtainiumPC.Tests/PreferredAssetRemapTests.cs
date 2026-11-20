using ObtainiumPC.Models;
using Xunit;

namespace ObtainiumPC.Tests;

public class PreferredAssetRemapTests
{
    private static List<KeyValuePair<string, string>> L(params string[] n) => n.Select(x => new KeyValuePair<string, string>(x, "u/" + x)).ToList();

    [Fact]
    public void Choice_follows_the_file_when_the_list_is_reordered()
        => Assert.Equal(0, TrackedApp.RemapPreferredIndex(L("a-x64.zip", "a-arm64.zip"), 1, L("a-arm64.zip", "a-x64.zip")));

    [Fact]
    public void Choice_follows_the_file_across_a_version_in_the_name()
        => Assert.Equal(1, TrackedApp.RemapPreferredIndex(L("app-1.0.0-x64.zip", "app-1.0.0-arm64.zip"), 1, L("app-1.1.0-x64.zip", "app-1.1.0-arm64.zip")));

    [Fact]
    public void Unknown_or_out_of_range_choice_falls_back_to_the_first_file()
    {
        Assert.Equal(0, TrackedApp.RemapPreferredIndex(L("a.zip"), 5, L("b.zip", "c.zip")));
        Assert.Equal(0, TrackedApp.RemapPreferredIndex(L("a.exe"), 0, L("b.zip", "c.zip")));
        Assert.Equal(0, TrackedApp.RemapPreferredIndex(L("a.exe"), 0, L()));
    }
}
