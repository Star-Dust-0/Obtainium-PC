using System;
using Microsoft.UI.Dispatching;

namespace ObtainiumPC.Services.Logging;

/// <summary>
/// Diagnostic only. A 500 ms timer on the UI thread notes when it fires much later than due, which means the
/// UI thread was busy or blocked in between. Logs the delay and the number of running downloads, nothing else.
/// </summary>
public sealed class UiStallWatch
{
    private const int IntervalMs = 500;
    private const int ReportOverMs = 2000;
    private readonly DispatcherQueueTimer _timer;
    private long _last = Environment.TickCount64;

    public UiStallWatch(DispatcherQueue queue)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            var late = now - _last - IntervalMs;
            _last = now;
            if (late > ReportOverMs)
                AppLogger.Warn($"UI thread was busy for about {late + IntervalMs} ms (timer {late} ms late); running downloads: {Download.DownloadRegistry.ActiveCount}");
        };
    }

    public void Start() { _last = Environment.TickCount64; _timer.Start(); }
}
