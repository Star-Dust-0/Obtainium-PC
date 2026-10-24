using System.Net;
using Xunit;

namespace ObtainiumPC.Tests;

/// Minimal stub handler: exact URL to response mapping, 404 otherwise.
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body, Dictionary<string, string> Headers)> _responses = new();

    public FixtureHttpHandler Respond(string url, string body, HttpStatusCode status = HttpStatusCode.OK,
        Dictionary<string, string>? headers = null)
    {
        _responses[url] = (status, body, headers ?? new Dictionary<string, string>());
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (_responses.TryGetValue(url, out var r))
        {
            var response = new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body) };
            foreach (var (key, value) in r.Headers)
                response.Headers.TryAddWithoutValidation(key, value);
            return Task.FromResult(response);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpClient Client(FixtureHttpHandler handler) => new(handler);
}
