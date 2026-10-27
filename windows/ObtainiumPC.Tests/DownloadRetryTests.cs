using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using ObtainiumPC.Services.Download;
using Xunit;

namespace ObtainiumPC.Tests;

/// Port of test/download_retry_test.dart intent: transient failures are retried, others are not.
public class DownloadRetryTests
{
    private static byte[] ValidPe()
    {
        var b = new byte[0x200];
        b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3c] = 0x80;
        b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
        return b;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    /// Serves statuses in order (last one repeats); a 200 serves a valid PE file. Returns the request count.
    private static async Task<(Exception? err, int requests)> Run(int[] statuses, int retries, string name)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var count = 0;
        using var stop = new CancellationTokenSource();
        var serve = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var status = statuses[Math.Min(count, statuses.Length - 1)];
                count++;
                ctx.Response.StatusCode = status;
                if (status == 200)
                {
                    var body = ValidPe();
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body);
                }
                ctx.Response.Close();
            }
        });
        Exception? err = null;
        try { await new DownloadService(retries, TimeSpan.FromMilliseconds(20)).DownloadAsync($"http://127.0.0.1:{port}/f", name); }
        catch (Exception e) { err = e; }
        stop.Cancel();
        listener.Stop();
        await Task.WhenAny(serve, Task.Delay(2000));
        return (err, count);
    }

    [Fact]
    public async Task ServerErrorThenSuccessIsRetried()
    {
        var (err, requests) = await Run(new[] { 503, 200 }, 3, "retry-ok.exe");
        Assert.Null(err);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task TooManyRequestsIsRetried()
    {
        var (err, requests) = await Run(new[] { 429, 429, 200 }, 3, "retry-429.exe");
        Assert.Null(err);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task NotFoundIsNotRetried()
    {
        var (err, requests) = await Run(new[] { 404 }, 3, "retry-404.exe");
        Assert.IsType<HttpRequestException>(err);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task RetriesAreBounded()
    {
        var (err, requests) = await Run(new[] { 500 }, 2, "retry-bounded.exe");
        Assert.IsType<HttpRequestException>(err);
        Assert.Equal(3, requests); // first try + 2 retries
    }

    [Fact]
    public async Task ZeroRetriesMeansSingleAttempt()
    {
        var (err, requests) = await Run(new[] { 500 }, 0, "retry-none.exe");
        Assert.IsType<HttpRequestException>(err);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void RetryClassification()
    {
        var none = CancellationToken.None;
        Assert.True(DownloadService.IsRetryable(new HttpRequestException("x"), none));
        Assert.True(DownloadService.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.BadGateway), none));
        Assert.False(DownloadService.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.Forbidden), none));
        Assert.False(DownloadService.IsRetryable(new InvalidDataException("web page"), none));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.False(DownloadService.IsRetryable(new TaskCanceledException(), cts.Token));
        Assert.True(DownloadService.IsRetryable(new TaskCanceledException(), none));
    }
}
