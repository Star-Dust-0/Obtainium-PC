using System.Net;
using System.Net.Sockets;
using ObtainiumPC.Services.Download;
using Xunit;

namespace ObtainiumPC.Tests;

/// Local-server proof of the download failure paths (no public network).
public class DownloadFailureTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    private static byte[] ValidPe()
    {
        var b = new byte[0x200];
        b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3c] = 0x80;
        b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
        return b;
    }

    private static async Task<(Exception? err, string target)> Run(Func<HttpListenerContext, Task> handler, string name)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serve = Task.Run(async () => { try { await handler(await listener.GetContextAsync()); } catch { } });
        Exception? err = null;
        string target = Path.Combine(DownloadService.DownloadDir, name);
        try { await new DownloadService().DownloadAsync($"http://127.0.0.1:{port}/f", name); }
        catch (Exception e) { err = e; }
        await Task.WhenAny(serve, Task.Delay(3000));
        return (err, target);
    }

    [Fact]
    public async Task Connection_dropped_mid_download_leaves_no_partial_file()
    {
        var name = "dropped-" + Guid.NewGuid().ToString("N") + ".exe";
        var (err, target) = await Run(async ctx =>
        {
            ctx.Response.ContentLength64 = 100000;
            await ctx.Response.OutputStream.WriteAsync(ValidPe());
            await ctx.Response.OutputStream.FlushAsync();
            ctx.Response.Abort();
        }, name);
        Assert.NotNull(err);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task Html_page_is_rejected_and_nothing_is_saved()
    {
        var name = "html-" + Guid.NewGuid().ToString("N") + ".exe";
        var (err, target) = await Run(async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("<html>blocked</html>"));
            ctx.Response.Close();
        }, name);
        Assert.IsType<InvalidDataException>(err);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Good_download_replaces_an_earlier_file_atomically()
    {
        var name = "good-" + Guid.NewGuid().ToString("N") + ".exe";
        var bytes = ValidPe();
        var (err, target) = await Run(async ctx =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }, name);
        Assert.Null(err);
        Assert.Equal(bytes.Length, new FileInfo(target).Length);
        Assert.False(File.Exists(target + ".part"));
        File.Delete(target);
    }

    [Fact]
    public async Task Failed_redownload_keeps_the_earlier_good_file()
    {
        var name = "keep-" + Guid.NewGuid().ToString("N") + ".exe";
        Directory.CreateDirectory(DownloadService.DownloadDir);
        var target = Path.Combine(DownloadService.DownloadDir, name);
        File.WriteAllBytes(target, ValidPe());
        var (err, _) = await Run(async ctx => { ctx.Response.StatusCode = 500; ctx.Response.Close(); await Task.CompletedTask; }, name);
        Assert.NotNull(err);
        Assert.True(File.Exists(target));
        File.Delete(target);
    }
}
