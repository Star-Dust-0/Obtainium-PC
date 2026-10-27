using System.Text.Json;
using ObtainiumPC.Models;
using Xunit;

namespace ObtainiumPC.Tests;

// Ports test/placeholder_add_test.dart buildPlaceholderApp cases.
public class PlaceholderAddTests
{
    private const string Url = "https://github.com/gz83/thorium";
    private static Dictionary<string, JsonElement> Settings(bool trackOnly) =>
        new() { ["trackOnly"] = JsonSerializer.SerializeToElement(trackOnly) };

    [Fact]
    public void BuildsPlaceholderWithUrlNameAndSourceAuthor()
    {
        var app = TrackedApp.BuildPlaceholder(Url, "GitHub", Settings(false));
        Assert.Equal(Url, app.Url);
        Assert.Equal(app.Url, app.Name);
        Assert.Equal("GitHub", app.Author);
        Assert.Single(app.AssetUrls);
        Assert.Equal("placeholder", app.AssetUrls[0].Key);
        Assert.Equal("placeholder", app.AssetUrls[0].Value);
        Assert.Equal(0, app.PreferredAssetIndex);
        Assert.Null(app.LastUpdateCheck);
        Assert.False(string.IsNullOrEmpty(app.LatestVersion));
        Assert.Matches("^[0-9a-f]{12}$", app.Id);
        Assert.Null(app.OverrideSource);
    }

    [Fact]
    public void TempIdIsDeterministicPerUrlAndSettings()
    {
        var a = TrackedApp.BuildPlaceholder(Url, "GitHub", Settings(false));
        var b = TrackedApp.BuildPlaceholder(Url, "GitHub", Settings(false));
        var c = TrackedApp.BuildPlaceholder(Url, "GitHub", Settings(true));
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public void RecordsOverrideSource()
    {
        var app = TrackedApp.BuildPlaceholder(Url, "GitHub", new(), "GitHub");
        Assert.Equal("GitHub", app.OverrideSource);
    }

    [Fact]
    public void PlaceholderSurvivesSaveAndLoad()
    {
        var app = TrackedApp.BuildPlaceholder(Url, "GitHub", Settings(true));
        var back = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app))!;
        Assert.Equal(app.Id, back.Id);
        Assert.Equal("placeholder", back.AssetUrls[0].Key);
        Assert.Equal("unknown", back.LatestVersion);
    }
}
