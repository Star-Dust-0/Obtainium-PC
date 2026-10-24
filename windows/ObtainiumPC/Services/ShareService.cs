using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace ObtainiumPC.Services;

/// <summary>
/// lib/pages/apps.dart + logs.dart SharePlus.instance.share -> the native
/// Windows share sheet via DataTransferManager. WinUI 3 unpackaged apps reach
/// it through the IDataTransferManagerInterop COM interface (the CsWinRT
/// projection ships no helper). Text shares fall back to the clipboard on
/// failure, mirroring the clipboard fallback upstream uses in logs.dart.
/// </summary>
public static class ShareService
{
    // Windows.ApplicationModel.DataTransfer.IDataTransferManager
    private static readonly Guid IDataTransferManagerIid =
        new("a5caee9b-8708-49d1-8d36-67d25a8da00c");
    // IDataTransferManagerInterop
    private static readonly Guid IDataTransferManagerInteropIid =
        new("3a3dcd6c-3eab-43dc-bcde-45671ce800c8");

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.HString)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId, [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.IUnknown)] out object factory);

    private static IDataTransferManagerInterop GetInterop()
    {
        const string className = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var hstr));
        try
        {
            var interopIid = IDataTransferManagerInteropIid;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstr, ref interopIid, out var factoryObj));
            return (IDataTransferManagerInterop)factoryObj;
        }
        finally
        {
            WindowsDeleteString(hstr);
        }
    }

    private static DataTransferManager GetDataTransferManagerForWindow(IDataTransferManagerInterop interop, IntPtr hwnd)
    {
        var dtmIid = IDataTransferManagerIid;
        return (DataTransferManager)interop.GetForWindow(hwnd, ref dtmIid);
    }

    public static void ShareText(string title, string text)
    {
        if (App.MainWindowInstance == null) return;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            var interop = GetInterop();
            var dtm = GetDataTransferManagerForWindow(interop, hwnd);
            dtm.DataRequested += (s, e) =>
            {
                e.Request.Data.Properties.Title = title;
                e.Request.Data.SetText(text);
            };
            interop.ShowShareUIForWindow(hwnd);
        }
        catch
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
    }

    public static async Task ShareFileAsync(string title, string filePath)
    {
        if (App.MainWindowInstance == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
        var interop = GetInterop();
        var dtm = GetDataTransferManagerForWindow(interop, hwnd);
        dtm.DataRequested += (s, e) =>
        {
            e.Request.Data.Properties.Title = title;
            e.Request.Data.SetStorageItems(new List<Windows.Storage.IStorageItem> { file });
        };
        interop.ShowShareUIForWindow(hwnd);
    }
}
