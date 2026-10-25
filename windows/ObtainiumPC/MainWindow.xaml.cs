using System;
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
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 700;
                presenter.PreferredMinimumHeight = 480;
            }
            Activated += MainWindow_Activated;
        }

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
