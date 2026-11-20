using System.Net;
using System.Text.Json;
using ObtainiumPC.Sources.GitHub;
using Xunit;

namespace ObtainiumPC.Tests;

public class GitHubEtagTests
{
    private const string Body = """
        [{"tag_name":"v1.0.0","name":"v1.0.0","prerelease":false,"draft":false,"published_at":"2026-09-01T00:00:00Z","body":"",
          "tarball_url":"t","zipball_url":"z",
          "assets":[{"name":"app-1.0.0-win-x64.exe","url":"https://api.github.com/repos/etag/app/releases/assets/1",
                     "browser_download_url":"https://github.com/etag/app/releases/download/v1.0.0/app-1.0.0-win-x64.exe"}]}]
        """;

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls, NotModified; public string? Mode;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            if (Mode == "limit")
            {
                var l = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
                l.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                l.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1900000000");
                return Task.FromResult(l);
            }
            if (r.Headers.TryGetValues("If-None-Match", out var v) && v.Contains("\"abc\""))
            { NotModified++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)); }
            var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
            ok.Headers.TryAddWithoutValidation("ETag", "\"abc\"");
            return Task.FromResult(ok);
        }
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> None = new Dictionary<string, JsonElement>();

    private static GitHubSource Src(HttpMessageHandler h) =>
        new(new GitHubSourceConfig(CheckRepoRename: false, UpdateHealthArchivedEnabled: false), new HttpClient(h));

    [Fact]
    public async Task Second_check_sends_the_ETag_and_reuses_the_cached_body_on_304()
    {
        var h = new Handler();
        var s = Src(h);
        var first = await s.GetLatestAssetDetailsAsync("https://github.com/etag/app", None);
        var second = await s.GetLatestAssetDetailsAsync("https://github.com/etag/app", None);
        Assert.Equal("v1.0.0", first.Version);
        Assert.Equal("v1.0.0", second.Version);
        Assert.Equal(1, h.NotModified);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Exhausted_rate_limit_reports_a_clear_message_not_a_bare_403()
    {
        var h = new Handler { Mode = "limit" };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Src(h).GetLatestAssetDetailsAsync("https://github.com/limit/app", None));
        Assert.Contains("rate limit", ex.Message);
    }

    [Fact]
    public void Cache_key_separates_tokens()
    {
        Assert.NotEqual(GitHubSource.EtagKey("u", "a"), GitHubSource.EtagKey("u", "b"));
        Assert.NotEqual(GitHubSource.EtagKey("u", null), GitHubSource.EtagKey("u", "a"));
    }
}
