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
            Activated += MainWindow_Activated;
        }

        private async void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
        {
            if (_backgroundStarted) return;
            _backgroundStarted = true;
            await App.InitAsync();
            Services.Updates.BackgroundUpdateService.Instance.Start();
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
                    "importexport" => typeof(ImportExportPage),
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
