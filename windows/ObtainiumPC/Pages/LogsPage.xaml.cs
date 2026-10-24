using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Services.Logging;

namespace ObtainiumPC.Pages;

public sealed partial class LogsPage : Page
{
    private int _days = 7;

    public LogsPage()
    {
        InitializeComponent();
            Loaded += (_, _) => Services.Localization.Localizer.Apply(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLogs();
    }

    private void LoadLogs()
    {
        if (LogsList == null) return; // SelectionChanged can fire during InitializeComponent
        LoadingBar.Visibility = Visibility.Visible;
        var logs = AppLogger.GetLogs(DateTimeOffset.Now.AddDays(-_days)).Reverse().ToList();
        // ThemeResource brushes may not be present on Application.Resources;
        // index access throws when the key is missing.
        Microsoft.UI.Xaml.Media.Brush? cardBrush = null;
        if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var brushObj)
            && brushObj is Microsoft.UI.Xaml.Media.Brush b)
            cardBrush = b;
        // lib/pages/logs.dart _levelColor: error / warning / debug / info get
        // distinct colors (upstream has no level filter, only colors).
        Microsoft.UI.Xaml.Media.Brush LevelBrush(AppLogLevel level) => level switch
        {
            AppLogLevel.Error => new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 99, 71)),
            AppLogLevel.Warning => new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 184, 77)),
            AppLogLevel.Debug => new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150)),
            _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 230, 230, 230)
                    : Windows.UI.Color.FromArgb(255, 30, 30, 30)),
        };
        LogsList.ItemsSource = logs.Select(l => new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(8),
            Background = cardBrush,
            Child = new TextBlock
            {
                Text = l.ToString(),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                Foreground = LevelBrush(l.Level),
            },
        }).ToList();
        EmptyState.Visibility = logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private void DaysCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DaysCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var days))
        {
            _days = days;
            LoadLogs();
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var logs = AppLogger.GetLogs(DateTimeOffset.Now.AddDays(-_days));
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(string.Join("\n\n", logs.Select(l => l.ToString())));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        if (sender is Button button)
        {
            button.Content = "Copied";
            await Task.Delay(1500);
            button.Content = "Copy all";
        }
    }

    // lib/pages/logs.dart _shareLogs: share sheet, clipboard fallback lives in
    // ShareService (mirrors upstream's copyToClipboard fallback).
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        var logs = AppLogger.GetLogs(DateTimeOffset.Now.AddDays(-_days));
        Services.ShareService.ShareText("App logs", string.Join("\n\n", logs.Select(l => l.ToString())));
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear logs",
            Content = "Delete all stored logs?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            AppLogger.ClearLogs();
            LoadLogs();
        }
    }
}
