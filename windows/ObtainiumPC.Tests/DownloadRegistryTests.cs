using ObtainiumPC.Services.Download;
using Xunit;

namespace ObtainiumPC.Tests;

public class DownloadRegistryTests
{
    [Fact]
    public void A_second_start_for_the_same_app_is_refused_until_the_first_ends()
    {
        var a = DownloadRegistry.TryBegin("app-1", "x.exe");
        Assert.NotNull(a);
        Assert.Null(DownloadRegistry.TryBegin("app-1", "x.exe"));
        Assert.Same(a, DownloadRegistry.Get("app-1"));
        DownloadRegistry.End(a);
        Assert.Null(DownloadRegistry.Get("app-1"));
        var b = DownloadRegistry.TryBegin("app-1", "x.exe");
        Assert.NotNull(b);
        DownloadRegistry.End(b);
    }

    [Fact]
    public void Different_apps_download_independently()
    {
        var a = DownloadRegistry.TryBegin("app-a", "a.exe");
        var b = DownloadRegistry.TryBegin("app-b", "b.exe");
        Assert.NotNull(a); Assert.NotNull(b);
        DownloadRegistry.End(a); DownloadRegistry.End(b);
    }

    [Fact]
    public void Progress_is_readable_by_a_page_that_opens_later_and_end_is_raised()
    {
        var e = DownloadRegistry.TryBegin("app-p", "p.exe")!;
        var changed = 0; var ended = 0;
        e.Changed += () => changed++;
        e.Ended += () => ended++;
        DownloadRegistry.Report(e, 10, 100);
        Assert.Equal((10L, (long?)100L), DownloadRegistry.Get("app-p")!.Snapshot);
        DownloadRegistry.End(e);
        Assert.Equal(1, changed); Assert.Equal(1, ended);
    }

    [Fact]
    public void Ending_a_stale_entry_does_not_remove_a_newer_one()
    {
        var first = DownloadRegistry.TryBegin("app-s", "s.exe")!;
        DownloadRegistry.End(first);
        var second = DownloadRegistry.TryBegin("app-s", "s.exe")!;
        DownloadRegistry.End(first);
        Assert.Same(second, DownloadRegistry.Get("app-s"));
        DownloadRegistry.End(second);
    }
}
