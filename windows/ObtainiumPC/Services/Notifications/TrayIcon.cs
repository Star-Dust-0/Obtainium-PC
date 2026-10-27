using System.Runtime.InteropServices;
using ObtainiumPC.Services.Download;
using ObtainiumPC.Services.Logging;

namespace ObtainiumPC.Services.Notifications;

/// <summary>
/// Optional notification-area icon (opt-in, default off). Plain Shell_NotifyIcon
/// on a window subclass; no extra packages.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const uint WM_APP_TRAY = 0x8000 + 17;
    const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2;
    const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    delegate IntPtr SubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATA d);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImageW(IntPtr inst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr m, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr m, uint flags, int x, int y, int r, IntPtr h, IntPtr rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr i);

    readonly IntPtr _hwnd;
    readonly SubclassProc _proc;
    readonly Action _open, _downloads, _check, _exit;
    IntPtr _icon;
    bool _added;

    public TrayIcon(IntPtr hwnd, Action open, Action downloads, Action check, Action exit)
    {
        _hwnd = hwnd; _open = open; _downloads = downloads; _check = check; _exit = exit;
        _proc = WndProc;
    }

    public bool IsVisible => _added;

    public void Show()
    {
        if (_added) return;
        try
        {
            _icon = LoadImageW(IntPtr.Zero, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            SetWindowSubclass(_hwnd, _proc, (UIntPtr)1, UIntPtr.Zero);
            var d = Data();
            _added = Shell_NotifyIconW(NIM_ADD, ref d);
            AppLogger.Info($"Tray icon added: {_added}");
        }
        catch (Exception ex) { AppLogger.Warn("Tray icon could not be added", ex); }
    }

    public void Hide()
    {
        if (!_added) return;
        try { var d = Data(); Shell_NotifyIconW(NIM_DELETE, ref d); RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)1); }
        catch (Exception ex) { AppLogger.Warn("Tray icon could not be removed", ex); }
        _added = false;
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    public void Dispose() { Hide(); _flyout?.Close(); _flyout = null; }

    NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_APP_TRAY, hIcon = _icon, szTip = "Obtainium-PC",
        szInfo = "", szInfoTitle = "",
    };

    IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_APP_TRAY)
        {
            var ev = (uint)(l.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP) _open();
            else if (ev == WM_RBUTTONUP || ev == WM_CONTEXTMENU) ShowMenu();
            return IntPtr.Zero;
        }
        return DefSubclassProc(h, msg, w, l);
    }

    void ShowMenu()
    {
        // Native-first: the WinUI 3 MenuFlyout (Windows 11 rounded menu, theme-following, acrylic when the
        // OS material is available). If it cannot be shown, fall back to the classic menu and log why,
        // so the tray is never left without a menu.
        try
        {
            GetCursorPos(out var p);
            _flyout ??= new TrayFlyoutMenu(_hwnd, Open, Downloads, CancelAll, Check, Exit);
            if (_flyout.Show(p.X, p.Y)) return;
        }
        catch (Exception ex) { AppLogger.Warn("WinUI tray menu failed; using classic menu", ex); }
        ShowClassicMenu();
    }

    TrayFlyoutMenu? _flyout;
    void Open() => _open();
    void Downloads() => _downloads();
    void CancelAll() => DownloadRegistry.CancelAll();
    void Check() => _check();
    void Exit() => _exit();

    void ShowClassicMenu()
    {
        var m = CreatePopupMenu();
        try
        {
            var active = DownloadRegistry.ActiveCount;
            AppendMenuW(m, MF_STRING, (UIntPtr)1, "Open Obtainium-PC");
            AppendMenuW(m, MF_STRING, (UIntPtr)2, active > 0 ? $"Downloads ({active} running)" : "Downloads");
            AppendMenuW(m, MF_STRING | (active > 0 ? 0u : MF_GRAYED), (UIntPtr)3, "Cancel all downloads");
            AppendMenuW(m, MF_STRING, (UIntPtr)4, "Check for updates");
            AppendMenuW(m, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(m, MF_STRING, (UIntPtr)5, "Exit");
            GetCursorPos(out var p);
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenu(m, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
            switch (cmd)
            {
                case 1: _open(); break;
                case 2: _downloads(); break;
                case 3: DownloadRegistry.CancelAll(); break;
                case 4: _check(); break;
                case 5: _exit(); break;
            }
        }
        finally { DestroyMenu(m); }
    }
}
