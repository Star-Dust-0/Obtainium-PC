using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Models;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Services.Updates;

namespace ObtainiumPC.Pages;

public sealed partial class UpdateHealthPage : Page
{
    private readonly AppSettings _settings = App.Settings;

    public UpdateHealthPage()
    {
        InitializeComponent();
        PopulateThresholds();
    }

    private void PopulateThresholds()
    {
        FillCombo(StaleReleaseCombo,
            UpdateHealthService.DayOptions.Select(d => KeyValuePair.Create(d, UpdateHealthService.DaysLabel(d))),
            _settings.UpdateHealthStaleReleaseDays);
        FillCombo(StaleVersionCombo,
            UpdateHealthService.DayOptions.Select(d => KeyValuePair.Create(d, UpdateHealthService.DaysLabel(d))),
            _settings.UpdateHealthStaleVersionDays);
        FillCombo(FailedChecksCombo,
            UpdateHealthService.FailedChecksOptions.Select(c => KeyValuePair.Create(c, UpdateHealthService.CountLabel(c, "checks"))),
            _settings.UpdateHealthFailedChecksCount);
        FillCombo(FilteredReleasesCombo,
            UpdateHealthService.FailedChecksOptions.Select(c => KeyValuePair.Create(c, UpdateHealthService.CountLabel(c, "releases"))),
            _settings.UpdateHealthFilteredReleasesCount);
        ArchivedToggle.IsOn = _settings.UpdateHealthArchivedEnabled;
        IndicatorToggle.IsOn = _settings.ShowUpdateHealthIndicator;

        StaleReleaseCombo.SelectionChanged += (s, e) => { _settings.UpdateHealthStaleReleaseDays = SelectedValue(StaleReleaseCombo); SaveAndRefresh(); };
        StaleVersionCombo.SelectionChanged += (s, e) => { _settings.UpdateHealthStaleVersionDays = SelectedValue(StaleVersionCombo); SaveAndRefresh(); };
        FailedChecksCombo.SelectionChanged += (s, e) => { _settings.UpdateHealthFailedChecksCount = SelectedValue(FailedChecksCombo); SaveAndRefresh(); };
        FilteredReleasesCombo.SelectionChanged += (s, e) => { _settings.UpdateHealthFilteredReleasesCount = SelectedValue(FilteredReleasesCombo); SaveAndRefresh(); };
        ArchivedToggle.Toggled += (s, e) => { _settings.UpdateHealthArchivedEnabled = ArchivedToggle.IsOn; SaveAndRefresh(); };
        IndicatorToggle.Toggled += (s, e) => { _settings.ShowUpdateHealthIndicator = IndicatorToggle.IsOn; SaveAndRefresh(); };
    }

    private static void FillCombo(ComboBox combo, IEnumerable<KeyValuePair<int, string>> options, int selected)
    {
        combo.ItemsSource = options.Select(o => o.Value).ToList();
        var list = options.ToList();
        var index = list.FindIndex(o => o.Key == selected);
        combo.SelectedIndex = index >= 0 ? index : 0;
        combo.Tag = list;
    }

    private static int SelectedValue(ComboBox combo) =>
        combo.Tag is List<KeyValuePair<int, string>> list && combo.SelectedIndex >= 0 && combo.SelectedIndex < list.Count
            ? list[combo.SelectedIndex].Key
            : 0;

    private void SaveAndRefresh()
    {
        _settings.Save();
        RefreshLists();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshLists();
    }

    private void RefreshLists()
    {
        var apps = App.TrackedApps.OrderBy(a => a.FinalName, StringComparer.OrdinalIgnoreCase).ToList();
        var flagged = apps.Where(a => UpdateHealthService.HasIssues(a, _settings)).ToList();
        var ignored = apps.Where(UpdateHealthService.IsIgnored).ToList();

        FlaggedList.ItemsSource = flagged.Select(BuildFlaggedRow).ToList();
        FlaggedEmpty.Visibility = flagged.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IgnoredList.ItemsSource = ignored.Select(BuildIgnoredRow).ToList();
        IgnoredEmpty.Visibility = ignored.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildFlaggedRow(TrackedApp app)
    {
        var config = UpdateHealthService.ResolveConfig(app.AdditionalSettings, _settings);
        var reasons = UpdateHealthService.EvaluateReasons(app, config);

        var menu = new MenuFlyout();
        var ignoreVersion = new MenuFlyoutItem { Text = "Ignore for this version" };
        ignoreVersion.Click += async (s, e) =>
        {
            app.UpdateHealthDismissedVersion = app.LatestVersion;
            await App.SaveAppsAsync();
            RefreshLists();
        };
        var ignoreAlways = new MenuFlyoutItem { Text = "Ignore permanently" };
        ignoreAlways.Click += async (s, e) =>
        {
            app.UpdateHealthEnabled = false;
            await App.SaveAppsAsync();
            RefreshLists();
        };
        menu.Items.Add(ignoreVersion);
        menu.Items.Add(ignoreAlways);

        var moreButton = new Button { Content = "…", Padding = new Thickness(10, 4, 10, 4) };
        moreButton.Click += (s, e) => menu.ShowAt(moreButton);

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = app.FinalName, Style = ThemeLookup.Style("BodyStrongTextBlockStyle") });
        text.Children.Add(new TextBlock
        {
            Text = string.Join("\n", reasons.Select(r => UpdateHealthService.ReasonLabel(r, app, config))),
            Foreground = ThemeLookup.Brush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(moreButton, 1);
        row.Children.Add(text);
        row.Children.Add(moreButton);
        return new Border
        {
            Child = row,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Background = ThemeLookup.Brush("CardBackgroundFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    private UIElement BuildIgnoredRow(TrackedApp app)
    {
        var restore = new Button { Content = "Restore", Padding = new Thickness(10, 4, 10, 4) };
        restore.Click += async (s, e) =>
        {
            app.UpdateHealthEnabled = true;
            app.UpdateHealthDismissedVersion = null;
            await App.SaveAppsAsync();
            RefreshLists();
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock
        {
            Text = app.FinalName,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeLookup.Brush("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(restore, 1);
        row.Children.Add(text);
        row.Children.Add(restore);
        return new Border
        {
            Child = row,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            Background = ThemeLookup.Brush("CardBackgroundFillColorDefaultBrush"),
            Margin = new Thickness(0, 0, 0, 6),
        };
    }
}
