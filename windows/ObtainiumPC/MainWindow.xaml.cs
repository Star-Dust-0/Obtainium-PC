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
            Services.Localization.Localizer.Apply(NavView);
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

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                Type? pageType = tag switch
                {
                    "apps" => typeof(AppsPage),
                    "addapp" => typeof(AddAppPage),
                    "updatehealth" => typeof(UpdateHealthPage),
                    "importexport" => typeof(ImportExportPage),
                    "logs" => typeof(LogsPage),
                    "settings" => typeof(SettingsPage),
                    _ => null
                };
                if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }
    }
}
