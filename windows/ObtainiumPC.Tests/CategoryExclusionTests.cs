using System.Linq;
using System.Text.Json;
using ObtainiumPC.Services.Settings;
using Xunit;

namespace ObtainiumPC.Tests;

// Port of test/category_exclusion_test.dart (+ the exclusion filter from
// getAppsSortedByUpdateCheckTime).
public class CategoryExclusionTests
{
    [Fact]
    public void DefaultsToNoExcludedCategories() =>
        Assert.Empty(new AppSettings().CategoriesExcludedFromChecks);

    [Fact]
    public void RoundTripsThroughJson()
    {
        var s = new AppSettings();
        s.CategoriesExcludedFromChecks.UnionWith(new[] { "Work", "Games" });
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(new[] { "Games", "Work" }, back.CategoriesExcludedFromChecks.OrderBy(x => x));
    }

    [Fact]
    public void OldSettingsWithoutTheFieldLoadAsEmpty()
    {
        var back = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Empty(back.CategoriesExcludedFromChecks);
    }

    [Fact]
    public void PrunesExclusionsForRemovedCategories()
    {
        var s = new AppSettings();
        s.CategoryColors["Games"] = unchecked((int)0xFF000000);
        s.CategoriesExcludedFromChecks.UnionWith(new[] { "Work", "Games" });
        s.PruneExcludedCategories();
        Assert.Equal(new[] { "Games" }, s.CategoriesExcludedFromChecks);
    }

    [Fact]
    public void AppInAnyExcludedCategoryIsSkipped()
    {
        var s = new AppSettings();
        s.CategoriesExcludedFromChecks.Add("Work");
        Assert.True(s.IsExcludedFromChecks(new[] { "Games", "Work" }));
        Assert.False(s.IsExcludedFromChecks(new[] { "Games" }));
        Assert.False(s.IsExcludedFromChecks(System.Array.Empty<string>()));
    }

    [Fact]
    public void NothingExcludedByDefault() =>
        Assert.False(new AppSettings().IsExcludedFromChecks(new[] { "Work" }));
}
