using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    const int GWL_EXSTYLE = -20;
    const long WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;

    readonly Action _open, _downloads, _cancelAll, _check, _exit;
    Window? _host;
    Grid? _anchor;

    public TrayFlyoutMenu(Action open, Action downloads, Action cancelAll, Action check, Action exit)
    { _open = open; _downloads = downloads; _cancelAll = cancelAll; _check = check; _exit = exit; }

    public bool Show(int x, int y)
    {
        if (_host == null)
        {
            _anchor = new Grid { Width = 1, Height = 1, Background = null };
            _host = new Window { Content = _anchor, Title = "Obtainium-PC tray menu" };
            var ap = _host.AppWindow;
            if (ap.Presenter is OverlappedPresenter op) { op.SetBorderAndTitleBar(false, false); op.IsResizable = false; op.IsMinimizable = false; op.IsMaximizable = false; }
            ap.IsShownInSwitchers = false;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_host);
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
            SetLayeredWindowAttributes(hwnd, 0, 0, 2); // LWA_ALPHA, fully transparent host
        }
        var host = _host; var anchor = _anchor!;
        host.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, 1, 1));
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
        string material = "none";
        try { flyout.SystemBackdrop = new DesktopAcrylicBackdrop(); material = "DesktopAcrylic"; }
        catch (Exception e) { AppLogger.Warn("Tray menu acrylic backdrop unavailable", e); }
        flyout.ShowAt(anchor);
        AppLogger.Info($"Tray menu shown (WinUI MenuFlyout, backdrop={material})");
        return true;
    }
}
