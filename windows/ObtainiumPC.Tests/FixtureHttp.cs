using System.Net;
using Xunit;

namespace ObtainiumPC.Tests;

/// Minimal stub handler: exact URL to response mapping, 404 otherwise.
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body, Dictionary<string, string> Headers)> _responses = new();
    private readonly List<(string Prefix, HttpStatusCode Status, string Body)> _prefixResponses = new();
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _sequences = new();

    public FixtureHttpHandler Respond(string url, string body, HttpStatusCode status = HttpStatusCode.OK,
        Dictionary<string, string>? headers = null)
    {
        _responses[url] = (status, body, headers ?? new Dictionary<string, string>());
        return this;
    }

    public FixtureHttpHandler RespondPrefix(string urlPrefix, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _prefixResponses.Add((urlPrefix, status, body));
        return this;
    }

    public FixtureHttpHandler RespondSequence(string url, params string[] bodies)
    {
        _sequences[url] = new Queue<(HttpStatusCode, string)>(bodies.Select(b => (HttpStatusCode.OK, b)));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (_sequences.TryGetValue(url, out var queue) && queue.Count > 0)
        {
            var (status, body) = queue.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
        foreach (var (prefix, status, body) in _prefixResponses)
            if (url.StartsWith(prefix, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
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
