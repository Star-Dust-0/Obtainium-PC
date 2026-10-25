namespace ObtainiumPC;

/// WinUI 3 unpackaged: setting RequestedTheme on the window root themes that
/// tree only - popup trees (ContentDialog, flyouts) keep the system theme,
/// so dialogs rendered light while the app was dark. This base applies the
/// app's saved theme to every dialog at construction.
public class ThemedContentDialog : Microsoft.UI.Xaml.Controls.ContentDialog
{
    public ThemedContentDialog()
    {
        RequestedTheme = App.Settings.Theme switch
        {
            "light" => Microsoft.UI.Xaml.ElementTheme.Light,
            "dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        };
        // Popup trees also ignore the window root's FlowDirection; mirror RTL
        // so titles, text and layout align right under Arabic.
        FlowDirection = Services.Localization.Loc.IsRtl
            ? Microsoft.UI.Xaml.FlowDirection.RightToLeft
            : Microsoft.UI.Xaml.FlowDirection.LeftToRight;
    }
}
