using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ObtainiumPC.Services.Localization;

namespace ObtainiumPC;

/// <summary>
/// Shown after a portable (.zip) app was extracted: says where it went and offers
/// "OK" or "Open folder" (opens the folder in Explorer).
/// </summary>
public static class ExtractedFolderDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot, string appName, string folder, bool usedFallbackRoot = false)
    {
        var text = string.Format(Loc.T("{0} was extracted to:"), appName) + "\n\n" + folder;
        if (usedFallbackRoot)
            text += "\n\n" + Loc.T("C:\\Obtainium could not be created, so the per-user folder was used instead.");
        var dialog = new ThemedContentDialog
        {
            Title = Loc.T("Extracted"),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = Loc.T("Open folder"),
            CloseButtonText = Loc.T("OK"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) OpenFolder(folder);
    }

    public static void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { Services.Logging.AppLogger.Warn("open folder failed", ex); }
    }
}
