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

public class DownloadCenterTests
{
    [Fact]
    public void Cancel_sets_the_token_of_that_job_only()
    {
        var a = DownloadRegistry.TryBegin("dc-a", "a.exe", "A")!;
        var b = DownloadRegistry.TryBegin("dc-b", "b.exe", "B")!;
        a.Cancel();
        Assert.True(a.Token.IsCancellationRequested);
        Assert.False(b.Token.IsCancellationRequested);
        DownloadRegistry.End(a, DownloadState.Cancelled, "Cancelled"); DownloadRegistry.End(b);
    }

    [Fact]
    public void Ended_job_moves_to_the_finished_list_with_its_state_and_a_new_job_replaces_it()
    {
        var a = DownloadRegistry.TryBegin("dc-c", "c.exe", "C")!;
        DownloadRegistry.End(a, DownloadState.Failed, "connection lost");
        Assert.Contains(DownloadRegistry.FinishedEntries(), e => e.AppId == "dc-c" && e.State == DownloadState.Failed && e.Message == "connection lost");
        Assert.Null(DownloadRegistry.Get("dc-c"));
        var again = DownloadRegistry.TryBegin("dc-c", "c.exe", "C")!;
        Assert.DoesNotContain(DownloadRegistry.FinishedEntries(), e => e.AppId == "dc-c");
        DownloadRegistry.End(again);
        DownloadRegistry.ClearFinished();
        Assert.DoesNotContain(DownloadRegistry.FinishedEntries(), e => e.AppId == "dc-c");
    }

    [Fact]
    public void Cancelling_a_finished_job_does_nothing()
    {
        var a = DownloadRegistry.TryBegin("dc-d", "d.exe")!;
        DownloadRegistry.End(a, DownloadState.Saved);
        a.Cancel();
        Assert.False(a.Token.IsCancellationRequested);
        Assert.Equal(DownloadState.Saved, a.State);
        DownloadRegistry.ClearFinished();
    }

    [Fact]
    public void Aggregate_progress_is_unknown_when_any_total_is_unknown()
    {
        var a = DownloadRegistry.TryBegin("dc-e", "e.exe")!;
        var b = DownloadRegistry.TryBegin("dc-f", "f.exe")!;
        a.Report(50, 100); b.Report(10, null);
        var agg = DownloadRegistry.Aggregate();
        Assert.Null(agg.Fraction); Assert.Equal(60, agg.Received);
        b.Report(25, 100);
        Assert.Equal(0.375, DownloadRegistry.Aggregate().Fraction);
        DownloadRegistry.End(a); DownloadRegistry.End(b); DownloadRegistry.ClearFinished();
    }

    [Fact]
    public void History_is_capped()
    {
        for (var i = 0; i < DownloadRegistry.HistoryLimit + 5; i++)
        {
            var e = DownloadRegistry.TryBegin("dc-h" + i, "h.exe")!;
            DownloadRegistry.End(e, DownloadState.Saved);
        }
        Assert.True(DownloadRegistry.FinishedEntries().Count <= DownloadRegistry.HistoryLimit);
        DownloadRegistry.ClearFinished();
    }
}
