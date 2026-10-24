using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Storage;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ObtainiumPC.Pages
{
    public sealed partial class ImportExportPage : Page
    {
        public ImportExportPage()
        {
            this.InitializeComponent();
        }

        private static void InitPicker(object picker)
        {
            if (App.MainWindowInstance == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        private void ShowResult(InfoBarSeverity severity, string title, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Title = title;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            await App.InitAsync();
            var picker = new FileSavePicker
            {
                SuggestedFileName = "obtainium-export",
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitPicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var level = ExportLevelCombo.SelectedIndex; // 0 apps, 1 +settings, 2 +creds
            Dictionary<string, object?>? settings = null;
            if (level > 0)
            {
                var el = JsonSerializer.SerializeToElement(App.Settings);
                settings = el.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
                if (level < 2) settings.Remove(nameof(Services.Settings.AppSettings.GitHubPersonalAccessToken));
            }
            var json = Services.Store.AppStore.Export(App.TrackedApps, settings, level, "0.1.0");
            await FileIO.WriteTextAsync(file, json);
            ShowResult(InfoBarSeverity.Success, "Export complete",
                $"{App.TrackedApps.Count} app(s) written to {file.Name}.");
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            await App.InitAsync();
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitPicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var (apps, hasSettings, _) = Services.Store.AppStore.Import(json);
                var added = 0;
                var replaced = 0;
                foreach (var app in apps)
                {
                    var i = App.TrackedApps.FindIndex(x => x.Id == app.Id);
                    if (i >= 0) { App.TrackedApps[i] = app; replaced++; }
                    else { App.TrackedApps.Add(app); added++; }
                }
                await App.SaveAppsAsync();
                var settingsNote = hasSettings
                    ? " The file also contains settings; applying them lands with the settings import step."
                    : "";
                ShowResult(InfoBarSeverity.Success, "Import complete",
                    $"{added} app(s) added, {replaced} replaced.{settingsNote}");
            }
            catch (Services.Store.ObtainiumImportException ex)
            {
                ShowResult(InfoBarSeverity.Error, "Import failed", ex.Message);
            }
            catch (Exception ex)
            {
                ShowResult(InfoBarSeverity.Error, "Import failed", ex.Message);
            }
        }
    }
}
