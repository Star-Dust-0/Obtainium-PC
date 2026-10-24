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
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadLogs();
    }

    private void LoadLogs()
    {
        LoadingBar.Visibility = Visibility.Visible;
        var logs = AppLogger.GetLogs(DateTimeOffset.Now.AddDays(-_days)).Reverse().ToList();
        LogsList.ItemsSource = logs.Select(l => new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(8),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = new TextBlock
            {
                Text = l.ToString(),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
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
