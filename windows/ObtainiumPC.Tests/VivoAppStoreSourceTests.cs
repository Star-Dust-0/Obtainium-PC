using System.Text.Json;
using ObtainiumPC.Sources.VivoAppStore;
using Xunit;

namespace ObtainiumPC.Tests;

public class VivoAppStoreSourceTests
{
    // lib/app_sources/vivoappstore.dart search(): maps result-list value[] items
    // to detail URLs keyed by id, skipping items without an id.
    [Fact]
    public async Task Search_maps_result_items_and_skips_idless()
    {
        var handler = new FixtureHttpHandler().RespondPrefix(
            "https://h5-api.appstore.vivo.com.cn/h5appstore/search/result-list",
            """
            {"code":0,"data":{"appSearchResponse":{"result":true,"value":[
                {"id":"12345","title_zh":"Cool App","developer":"Cool Dev"},
                {"title_zh":"No Id","developer":"Nobody"}
            ]}}}
            """);
        var results = await new VivoAppStoreSource(FixtureHttpHandler.Client(handler))
            .SearchAsync("cool", new Dictionary<string, JsonElement>());
        var hit = Assert.Single(results!);
        Assert.Equal("https://detail-browser.vivo.com.cn/v115/index.html?appId=12345", hit.Key);
        Assert.Equal(new[] { "Cool App", "Cool Dev" }, hit.Value);
    }

    // Upstream throws NoReleasesError when the search envelope reports failure.
    [Fact]
    public async Task Search_throws_on_failure_envelope()
    {
        var handler = new FixtureHttpHandler().RespondPrefix(
            "https://h5-api.appstore.vivo.com.cn/h5appstore/search/result-list",
            """{"code":1,"data":{"appSearchResponse":{"result":false}}}""");
        await Assert.ThrowsAsync<ObtainiumPC.Sources.NoReleasesException>(() =>
            new VivoAppStoreSource(FixtureHttpHandler.Client(handler))
                .SearchAsync("cool", new Dictionary<string, JsonElement>()));
    }
}
