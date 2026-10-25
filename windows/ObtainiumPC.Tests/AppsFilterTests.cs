using ObtainiumPC.Models;
using ObtainiumPC.Services.Store;
using Xunit;

namespace ObtainiumPC.Tests;

/// AppsFilter (lib/components/app_list_tile.dart): the apps-list filter
/// dialog model. Every set field narrows the list.
public class AppsFilterTests
{
    private static TrackedApp App(
        string name = "Notepad++", string author = "Don Ho", string id = "npp",
        string? installed = "8.9.8.1", string[]? categories = null) => new()
        {
            Id = id,
            Name = name,
            Author = author,
            Url = "https://github.com/notepad-plus-plus/notepad-plus-plus",
            InstalledVersion = installed,
            Categories = (categories ?? new string[] { }).ToList(),
        };

    [Fact]
    public void Default_filter_matches_everything()
    {
        var f = new AppsFilter();
        Assert.True(f.IsDefault);
        Assert.True(f.Matches(App(), hasUpdate: false, sourceName: "GitHub"));
        Assert.True(f.Matches(App(installed: null), hasUpdate: true, sourceName: "F-Droid"));
    }

    [Theory]
    [InlineData("notepad", true)]   // case-insensitive name match
    [InlineData("NOTEPAD", true)]
    [InlineData("vscode", false)]
    public void Name_filter_matches_name_only(string query, bool expected)
    {
        var f = new AppsFilter { NameFilter = query };
        Assert.Equal(expected, f.Matches(App(), hasUpdate: true, sourceName: "GitHub"));
    }

    [Fact]
    public void Author_and_id_filters_apply()
    {
        Assert.True(new AppsFilter { AuthorFilter = "don" }.Matches(App(), true, "GitHub"));
        Assert.False(new AppsFilter { AuthorFilter = "imran" }.Matches(App(), true, "GitHub"));
        Assert.True(new AppsFilter { IdFilter = "np" }.Matches(App(), true, "GitHub"));
        Assert.False(new AppsFilter { IdFilter = "obt" }.Matches(App(), true, "GitHub"));
    }

    [Fact]
    public void IncludeUptodate_off_hides_apps_without_updates()
    {
        var f = new AppsFilter { IncludeUptodate = false };
        Assert.False(f.Matches(App(), hasUpdate: false, sourceName: "GitHub"));
        Assert.True(f.Matches(App(), hasUpdate: true, sourceName: "GitHub"));
    }

    [Fact]
    public void IncludeNonInstalled_off_hides_apps_without_recorded_install()
    {
        var f = new AppsFilter { IncludeNonInstalled = false };
        Assert.False(f.Matches(App(installed: null), true, "GitHub"));
        Assert.True(f.Matches(App(installed: "8.9.8.1"), true, "GitHub"));
    }

    [Fact]
    public void Category_filter_matches_any_membership()
    {
        var f = new AppsFilter { CategoryFilter = new HashSet<string> { "tools", "dev" } };
        Assert.True(f.Matches(App(categories: new[] { "dev" }), true, "GitHub"));
        Assert.False(f.Matches(App(categories: new[] { "media" }), true, "GitHub"));
        Assert.False(f.Matches(App(categories: null), true, "GitHub"));
    }

    [Fact]
    public void Source_filter_matches_resolved_source_name_case_insensitively()
    {
        var f = new AppsFilter { SourceFilter = "GitHub" };
        Assert.True(f.Matches(App(), true, "github"));
        Assert.False(f.Matches(App(), true, "GitLab"));
        Assert.False(f.Matches(App(), true, ""));
    }
}
