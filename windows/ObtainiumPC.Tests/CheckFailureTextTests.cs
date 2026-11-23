using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ObtainiumPC.Models;
using ObtainiumPC.Sources;
using ObtainiumPC.Services.Updates;
using Xunit;

namespace ObtainiumPC.Tests;

public class CheckFailureTextTests
{
    private static string Id(string s) => s;

    [Fact] public void NoReason_KeepsLegacyText() =>
        Assert.Equal("Last check failed (3x)", CheckFailureText.Format(3, null, Id));

    [Fact] public void HttpCategory_ShowsStatusCodeOnly() =>
        Assert.Equal("Last check failed (1x): Server request failed (HTTP 404)",
            CheckFailureText.Format(1, "http_404", Id));

    [Fact] public void UnknownCategory_FallsBackToGeneric() =>
        Assert.Equal("Last check failed (2x): Unknown error - details in Logs",
            CheckFailureText.Format(2, "garbage; DROP TABLE apps", Id));

    [Fact] public void MalformedHttpCategory_FallsBackToGeneric() =>
        Assert.Equal("Last check failed (2x): Unknown error - details in Logs",
            CheckFailureText.Format(2, "http_abc", Id));

    [Fact] public void CategoryFor_HttpException_KeepsOnlyStatusCode() =>
        Assert.Equal("http_404", CheckFailureText.CategoryFor(
            new ObtainiumHttpException(404, "https://github.com/private-org/secret-repo?token=abc123")));

    [Fact] public void CategoryFor_RateLimit() =>
        Assert.Equal("rate_limited", CheckFailureText.CategoryFor(
            new InvalidOperationException("GitHub rate limit reached, resets at 5:00 PM. Add a personal access token in Settings to raise the limit.")));

    [Fact] public void CategoryFor_KnownKinds()
    {
        Assert.Equal("no_suitable_release", CheckFailureText.CategoryFor(new NoReleasesException()));
        Assert.Equal("no_installer_asset", CheckFailureText.CategoryFor(new NoAppAssetException()));
        Assert.Equal("repository_moved", CheckFailureText.CategoryFor(new RepositoryRenamedException("https://a", "https://b")));
        Assert.Equal("invalid_url", CheckFailureText.CategoryFor(new InvalidUrlException("GitHub")));
        Assert.Equal("unsupported_source", CheckFailureText.CategoryFor(new AndroidSourceNotSupportedException()));
        Assert.Equal("network_error", CheckFailureText.CategoryFor(new HttpRequestException("connect failed token=hunter2")));
        Assert.Equal("timeout", CheckFailureText.CategoryFor(new TaskCanceledException()));
        Assert.Equal("unknown", CheckFailureText.CategoryFor(new Exception("weird https://x/y?key=1")));
    }

    [Fact] public void PersistedCategory_NeverLeaksMessageOrUrl()
    {
        var secret = "https://user:pw@github.com/private-org/secret-repo/releases?token=abc123XYZ";
        var cat = CheckFailureText.CategoryFor(new ObtainiumHttpException(404, secret));
        Assert.DoesNotContain("abc123XYZ", cat);
        Assert.DoesNotContain("secret-repo", cat);
        Assert.DoesNotContain("private-org", cat);
        Assert.Equal("unknown", CheckFailureText.CategoryFor(new Exception(secret)));
    }

    [Fact] public void LastCheckError_RoundTripsThroughJson()
    {
        var app = new TrackedApp { Id = "a", Url = "https://github.com/x/y", ConsecutiveFailedChecks = 2, LastCheckError = "http_404" };
        var back = JsonSerializer.Deserialize<TrackedApp>(JsonSerializer.Serialize(app));
        Assert.NotNull(back);
        Assert.Equal(2, back!.ConsecutiveFailedChecks);
        Assert.Equal("http_404", back.LastCheckError);
    }

    [Fact] public void LastCheckError_DefaultsToNull_ForOldStores()
    {
        var back = JsonSerializer.Deserialize<TrackedApp>("{\"id\":\"a\",\"url\":\"u\",\"consecutiveFailedChecks\":5}");
        Assert.NotNull(back);
        Assert.Equal(5, back!.ConsecutiveFailedChecks);
        Assert.Null(back.LastCheckError);
    }
}
