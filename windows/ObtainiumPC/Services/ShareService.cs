using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace ObtainiumPC.Services;

/// <summary>
/// lib/pages/apps.dart + logs.dart SharePlus.instance.share -> the native
/// Windows share sheet via DataTransferManager (WinUI 3 interop). Text
/// shares fall back to the clipboard on failure, mirroring the clipboard
/// fallback upstream uses in logs.dart.
/// </summary>
public static class ShareService
{
    // IID of Windows.ApplicationModel.DataTransfer.IDataTransferManager
    private static readonly Guid IDataTransferManagerIid =
        new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    public static void ShareText(string title, string text)
    {
        if (App.MainWindowInstance == null) return;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            var dtm = WinRT.Interop.DataTransferManagerInterop.GetForWindow(hwnd, IDataTransferManagerIid);
            dtm.DataRequested += (s, e) =>
            {
                e.Request.Data.Properties.Title = title;
                e.Request.Data.SetText(text);
            };
            WinRT.Interop.DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
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
        var dtm = WinRT.Interop.DataTransferManagerInterop.GetForWindow(hwnd, IDataTransferManagerIid);
        dtm.DataRequested += (s, e) =>
        {
            e.Request.Data.Properties.Title = title;
            e.Request.Data.SetStorageItems(new List<Windows.Storage.IStorageItem> { file });
        };
        WinRT.Interop.DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }
}
