using System.Runtime.InteropServices;

namespace ObtainiumPC.Services.Download;

/// <summary>Taskbar button progress (ITaskbarList3). Best effort: any COM failure is ignored.</summary>
public static class TaskbarProgress
{
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListClass { }

    private const int NoProgress = 0, Indeterminate = 1, Normal = 2;
    private static ITaskbarList3? _list;

    /// fraction in [0,1], or null for indeterminate; active=false clears the progress.
    public static void Set(IntPtr hwnd, bool active, double? fraction)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            _list ??= (ITaskbarList3)new TaskbarListClass();
            _list.HrInit();
            if (!active) { _list.SetProgressState(hwnd, NoProgress); return; }
            if (fraction is { } f)
            {
                _list.SetProgressState(hwnd, Normal);
                _list.SetProgressValue(hwnd, (ulong)Math.Round(Math.Clamp(f, 0, 1) * 1000), 1000);
            }
            else _list.SetProgressState(hwnd, Indeterminate);
        }
        catch { /* taskbar progress is cosmetic */ }
    }
}
