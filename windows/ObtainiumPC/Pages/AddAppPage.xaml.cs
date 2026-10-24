using System;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Sources;

namespace ObtainiumPC.Pages
{
    public sealed partial class AddAppPage : Page
    {
        public AddAppPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = (string?)null });
            foreach (var name in SourceCatalog.OverrideSourceNames)
                OverrideSourceCombo.Items.Add(new ComboBoxItem { Content = name, Tag = (string?)name });
            OverrideSourceCombo.SelectedIndex = 0;
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (url.Length == 0)
            {
                Show(InfoBarSeverity.Warning, "Enter a source URL first.");
                return;
            }
            AddButton.IsEnabled = false;
            BusyRing.IsActive = true;
            try
            {
                var settings = new Dictionary<string, JsonElement>();
                if (TrackOnlyBox.IsChecked == true)
                    settings["trackOnly"] = JsonSerializer.SerializeToElement(true);
                var overrideSource = (OverrideSourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
                var app = await App.Updater.CheckAppAsync(url, settings, overrideSource: overrideSource);
                App.TrackedApps.Add(app);
                await App.SaveAppsAsync();
                Show(InfoBarSeverity.Success, $"Added {app.FinalName} (latest: {app.LatestVersion ?? "unknown"}).");
                UrlBox.Text = "";
            }
            catch (InvalidUrlException)
            {
                Show(InfoBarSeverity.Error, "That URL is not a supported source. Pick an override source above for other pages (for example HTML).");
            }
            catch (Exception ex)
            {
                Show(InfoBarSeverity.Error, ex.Message);
            }
            finally
            {
                BusyRing.IsActive = false;
                AddButton.IsEnabled = true;
            }
        }

        private void Show(InfoBarSeverity severity, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }
    }
}
