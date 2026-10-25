using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ObtainiumPC.Pages;

namespace ObtainiumPC
{
    public sealed partial class MainWindow : Window
    {
        private bool _backgroundStarted;

        public MainWindow()
        {
            this.InitializeComponent();
            Title = "Obtainium-PC";
            SystemBackdrop = new MicaBackdrop();
            // Below ~700px the Apps header controls can no longer all fit even
            // with the search box flexing; clamp instead of clipping them.
            SetMinimumTrackSize();
            Activated += MainWindow_Activated;
        }

        // OverlappedPresenter has no min-size API in this SDK; clamp via
        // WM_GETMINMAXINFO instead (DPI-scaled physical pixels).
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _newWndProc;
        private IntPtr _oldWndProc;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int GWLP_WNDPROC = -4;

        private struct POINT { public int x; public int y; }
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private void SetMinimumTrackSize()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var scale = GetDpiForWindow(hWnd) / 96.0;
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize = new POINT { x = (int)(700 * scale), y = (int)(480 * scale) };
                Marshal.StructureToPtr(mmi, lParam, false);
            }
            return CallWindowProcW(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private async void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            if (_backgroundStarted) return;
            _backgroundStarted = true;
            await App.InitAsync();
            // The saved language is only known after InitAsync; re-apply now.
            ApplyLocalization();
            // Apply the saved theme: SettingsPage only sets RequestedTheme when
            // the user changes the combo, so without this a saved "dark" came
            // back light after every restart.
            if (Content is FrameworkElement root)
                root.RequestedTheme = App.Settings.Theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
            Services.Updates.BackgroundUpdateService.Instance.Start();
            // obtainium:// protocol registration follows the setting (HKCU,
            // no admin, reversible from Settings).
            Services.DeepLinkService.ApplyRegistrationSetting();
            if (App.PendingDeepLink is { } link)
            {
                App.PendingDeepLink = null;
                _ = HandleDeepLinkAsync(link);
            }
        }

        public async System.Threading.Tasks.Task HandleDeepLinkAsync(string link)
        {
            await Services.DeepLinkService.InterpretAsync(link, this);
        }

        public void NavigateToApps()
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            if (ContentFrame.CurrentSourcePageType != typeof(AppsPage))
                ContentFrame.Navigate(typeof(AppsPage));
        }

        // home.dart goToExistingApp.
        public void NavigateToApp(string appId)
        {
            NavigateToApps();
            ContentFrame.Navigate(typeof(AppDetailPage), appId);
        }

        // home.dart goToAddApp(initialUrl: data).
        public void NavigateToAddApp(string prefillUrl)
        {
            NavView.SelectedItem = NavView.MenuItems[1];
            ContentFrame.Navigate(typeof(AddAppPage), prefillUrl);
        }

        // Re-applies translations and flow direction after a language switch;
        // pages are recreated from their XAML literals by re-navigation.
        public void ApplyLocalization()
        {
            try
            {
            // Translate the persistent item objects first: the tree walk only
            // reaches realized visuals; nav item containers can be unrealized
            // or mid re-template after the flow-direction flip, so the walk
            // alone can miss them and leave stale labels after a language
            // switch (ui-smoke scenario 17 evidence).
            Services.Localization.Localizer.ApplyToItems(NavView.MenuItems);
            Services.Localization.Localizer.ApplyToItems(NavView.FooterMenuItems);
            Services.Localization.Localizer.Apply(NavView);
            var dbg = new System.Text.StringBuilder("apply-localization nav-items end state: ");
            foreach (var mi in NavView.MenuItems)
                if (mi is Microsoft.UI.Xaml.Controls.NavigationViewItem nvi2)
                    dbg.Append($"[{nvi2.Tag}]={nvi2.Content}; ");
            foreach (var mi in NavView.FooterMenuItems)
                if (mi is Microsoft.UI.Xaml.Controls.NavigationViewItem nvi3)
                    dbg.Append($"[{nvi3.Tag}]={nvi3.Content}; ");
            Services.Logging.AppLogger.Info(dbg.ToString());
            var current = NavView.SelectedItem as Microsoft.UI.Xaml.Controls.NavigationViewItem;
            var pageType = current?.Tag switch
            {
                "apps" => typeof(AppsPage),
                "addapp" => typeof(AddAppPage),
                "updatehealth" => typeof(UpdateHealthPage),
                "importexport" => typeof(ImportExportPage),
                "logs" => typeof(LogsPage),
                "settings" => typeof(SettingsPage),
                _ => null
            };
            if (pageType != null)
                ContentFrame.Navigate(pageType);
            }
            catch (System.Exception ex)
            {
                Services.Logging.AppLogger.Error("ApplyLocalization failed", ex);
                throw;
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private static Type? TagToPage(string tag) => tag switch
        {
            "apps" => typeof(AppsPage),
            "addapp" => typeof(AddAppPage),
            "updatehealth" => typeof(UpdateHealthPage),
            "importexport" => typeof(ImportExportPage),
            "logs" => typeof(LogsPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                Type? pageType = TagToPage(tag);
                if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }

        // SelectionChanged only fires when the selection CHANGES. Tapping the
        // already-selected nav item from a pushed page (app detail, options)
        // must still return to that section's root, matching upstream where
        // the tab always leads back to its list (the upstream detail also
        // shows a leading BackButton, lib/pages/app.dart:469-471). Without
        // this the user is stranded on the detail page: the current item is
        // already selected, so no SelectionChanged ever fires.
        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
            {
                Type? pageType = TagToPage(tag);
                if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }
    }
}
