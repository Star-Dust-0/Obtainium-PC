using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ObtainiumPC.Services.Download;
using ObtainiumPC.Services.Logging;

namespace ObtainiumPC.Services.Notifications;

/// <summary>
/// The tray menu as a real WinUI 3 MenuFlyout (Windows 11 context-menu design: rounded corners, theme,
/// hover states, icons). A MenuFlyout needs a XAML root to open from, so a 1x1 fully transparent tool
/// window at the cursor hosts it, the same technique used by tray libraries. Must run on the UI thread.
/// </summary>
internal sealed class TrayFlyoutMenu
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
    static string DescribeForeground()
    {
        try
        {
            var h = GetForegroundWindow(); var cls = new System.Text.StringBuilder(128); var ttl = new System.Text.StringBuilder(128);
            GetClassName(h, cls, 128); GetWindowText(h, ttl, 128); GetWindowThreadProcessId(h, out var pid);
            string pn; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { pn = "?"; }
            return $"foreground hwnd={h} class='{cls}' title='{ttl}' process={pn}({pid}) ownPid={Environment.ProcessId}";
        }
        catch (Exception e) { return "foreground unknown: " + e.Message; }
    }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint p);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(CursorPoint p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
    readonly IntPtr _owner;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;

    readonly Action _open, _downloads, _cancelAll, _check, _exit;
    Window? _host;
    Grid? _anchor;
    MenuFlyout? _current;

    /// The hidden host is a real window: it must be closed on exit or the process never ends.
    public void Close()
    {
        try { var h = _host; _host = null; _current = null; h?.Close(); } catch (Exception e) { AppLogger.Warn("Tray menu host close failed", e); }
    }

    public TrayFlyoutMenu(IntPtr owner, Action open, Action downloads, Action cancelAll, Action check, Action exit)
    { _owner = owner; _open = open; _downloads = downloads; _cancelAll = cancelAll; _check = check; _exit = exit; }

    public bool Show(int x, int y)
    {
        if (_host == null)
        {
            _anchor = new Grid { Width = 1, Height = 1, Background = null };
            _host = new Window { Content = _anchor, Title = "Obtainium-PC tray menu" };
            var ap = _host.AppWindow;
            if (ap.Presenter is OverlappedPresenter op) { op.SetBorderAndTitleBar(false, false); op.IsResizable = false; op.IsMinimizable = false; op.IsMaximizable = false; }
            ap.IsShownInSwitchers = false;
            // Clicking another window leaves the flyout open (light dismiss only covers this XAML root): hide it
            // when the host loses activation.
            _host.Activated += (_, a) =>
            {
                if (a.WindowActivationState == WindowActivationState.Deactivated && _current != null)
                {
                    AppLogger.Info("Tray menu hidden: focus moved to another window; " + DescribeForeground());
                    try { _current.Hide(); } catch { }
                }
            };
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host);
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
            SetLayeredWindowAttributes(hwnd, 0, 0, 2); // LWA_ALPHA, fully transparent host
        }
        var host = _host; var anchor = _anchor!;
        host.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, Math.Max(0, y - 64), 1, 1));
        host.Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(host));

        var active = DownloadRegistry.ActiveCount;
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.TopEdgeAlignedRight };
        MenuFlyoutItem Item(string text, string glyph, Action a, bool enabled = true)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            it.Click += (_, _) => host.DispatcherQueue.TryEnqueue(() => { try { a(); } catch (Exception e) { AppLogger.Warn("Tray menu action failed", e); } });
            return it;
        }
        flyout.Items.Add(Item("Open Obtainium-PC", "\uE8A7", _open));
        flyout.Items.Add(Item(active > 0 ? $"Downloads ({active} running)" : "Downloads", "\uE896", _downloads));
        flyout.Items.Add(Item("Cancel all downloads", "\uE711", _cancelAll, active > 0));
        flyout.Items.Add(Item("Check for updates", "\uE72C", _check));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item("Exit", "\uE7E8", _exit));
        flyout.Closed += (_, _) => host.DispatcherQueue.TryEnqueue(() => { try { host.AppWindow.Hide(); } catch { } });
        // The default presenter paints its own opaque surface over any system backdrop. Make the
        // presenter background a light tint so the acrylic behind the popup can show through.
        try
        {
            var dark = (anchor.ActualTheme == ElementTheme.Dark);
            var tint = dark ? Windows.UI.Color.FromArgb(0x66, 0x20, 0x20, 0x20) : Windows.UI.Color.FromArgb(0x66, 0xF3, 0xF3, 0xF3);
            var style = new Style(typeof(MenuFlyoutPresenter));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            flyout.MenuFlyoutPresenterStyle = style;
        }
        catch (Exception e) { AppLogger.Warn("Tray menu presenter tint failed", e); }
        try
        {
            AppLogger.Info($"Tray acrylic environment: supported={Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported()}, advancedEffects={new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled}, energySaver={Windows.System.Power.PowerManager.EnergySaverStatus}");
        }
        catch (Exception e) { AppLogger.Warn("Tray acrylic environment unavailable", e); }
        string material = "none";
        try { flyout.SystemBackdrop = new DesktopAcrylicBackdrop(); material = "DesktopAcrylic"; }
        catch (Exception e) { AppLogger.Warn("Tray menu acrylic backdrop unavailable", e); }
        _current = flyout;
        var t0 = DateTime.UtcNow;
        var dismissTimer = host.DispatcherQueue.CreateTimer();
        dismissTimer.Interval = TimeSpan.FromMilliseconds(150);
        dismissTimer.Tick += (_, _) =>
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            bool clickedOutside = false;
            if ((GetAsyncKeyState(1) & 0x8001) != 0)
            {
                GetCursorPos(out var cursor);
                var target = GetAncestor(WindowFromPoint(cursor), 2); // GA_ROOT
                GetWindowThreadProcessId(target, out var targetPid);
                clickedOutside = target == _owner || targetPid != (uint)Environment.ProcessId;
            }
            if (pid != (uint)Environment.ProcessId || clickedOutside)
            {
                AppLogger.Info("Tray menu dismissed: foreground left app");
                dismissTimer.Stop();
                flyout.Hide();
            }
        };
        flyout.Opened += (_, _) => { GetAsyncKeyState(1); AppLogger.Info("Tray menu Opened"); dismissTimer.Start(); };
        flyout.Closed += (_, _) => dismissTimer.Stop();
        flyout.Closed += (_, _) => AppLogger.Info($"Tray menu Closed after {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms");
        void ShowNow()
        {
            try { flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedRight, Position = new Windows.Foundation.Point(0, -80), ShowMode = FlyoutShowMode.Standard }); AppLogger.Info($"Tray menu shown (WinUI MenuFlyout, backdrop={material}, host loaded={anchor.IsLoaded}, xamlRoot={anchor.XamlRoot != null})"); }
            catch (Exception e) { AppLogger.Warn("Tray menu ShowAt failed", e); }
        }
        // The first time, the host's XAML is not loaded yet; opening before that can show nothing.
        if (anchor.IsLoaded && anchor.XamlRoot != null) ShowNow();
        else { RoutedEventHandler? h = null; h = (_, _) => { anchor.Loaded -= h; ShowNow(); }; anchor.Loaded += h; }
        return true;
    }
}
