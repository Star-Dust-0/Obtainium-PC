using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ObtainiumPC.Pages
{
    /// Global settings, saved immediately. Mirrors the original's settings keys
    /// that the engine already consumes; the rest arrive with their pages.
    public sealed partial class SettingsPage : Page
    {
        private bool _loading;

        private static readonly (string Label, string Value)[] ThemeOptions =
            { ("System default", "system"), ("Light", "light"), ("Dark", "dark") };

        private static readonly (string Label, int Minutes)[] IntervalOptions =
            { ("15 minutes", 15), ("30 minutes", 30), ("1 hour", 60), ("6 hours", 360), ("12 hours", 720), ("1 day", 1440) };

        private static readonly int[] MinAgeOptions = { 0, 1, 2, 3, 5, 7, 14, 30 };

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadValues();
        }

        private void LoadValues()
        {
            _loading = true;
            var s = App.Settings;

            foreach (var (label, value) in ThemeOptions) ThemeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            SortByCombo.Items.Add("As added");
            SortByCombo.Items.Add("Name (Author)");
            SortByCombo.Items.Add("Author (Name)");
            SortByCombo.Items.Add("Release date");
            SortByCombo.SelectedIndex = Math.Clamp(App.Settings.SortColumn, 0, 3);
            SortOrderCombo.Items.Add("Ascending");
            SortOrderCombo.Items.Add("Descending");
            SortOrderCombo.SelectedIndex = App.Settings.SortOrder == 1 ? 1 : 0;
            PinUpdatesToggle.IsOn = App.Settings.PinUpdates;
            BuryNonInstalledToggle.IsOn = App.Settings.BuryNonInstalled;
            AddWithoutCheckToggle.IsOn = App.Settings.AllowAddingAppsWithoutCheck;
            ThemeCombo.SelectedIndex = Math.Max(0, Array.FindIndex(ThemeOptions, o => o.Value == s.Theme));

            foreach (var (label, minutes) in IntervalOptions) UpdateIntervalCombo.Items.Add(new ComboBoxItem { Content = label, Tag = minutes });
            var ii = Array.FindIndex(IntervalOptions, o => o.Minutes == s.UpdateIntervalMinutes);
            UpdateIntervalCombo.SelectedIndex = ii >= 0 ? ii : 3;

            foreach (var d in MinAgeOptions) MinAgeCombo.Items.Add(new ComboBoxItem { Content = d == 0 ? "None" : d == 1 ? "1 day" : $"{d} days", Tag = d });
            var mi = Array.IndexOf(MinAgeOptions, s.MinimumUpdateAgeDays);
            MinAgeCombo.SelectedIndex = mi >= 0 ? mi : 0;

            CheckOnStartToggle.IsOn = s.CheckOnStart;
            NotifyToggle.IsOn = s.NotifyUpdates;
            HideDowngradesToggle.IsOn = s.HideDowngrades;
            GlobalVersionRegexBox.Text = s.GlobalVersionExtractionRegEx ?? "";
            GlobalMatchGroupBox.Text = s.GlobalMatchGroupToUse ?? "";
            GlobalAssetFilterBox.Text = s.GlobalAssetFilterRegEx ?? "";
            GitHubPatBox.Password = s.GitHubPersonalAccessToken ?? "";
            RepoRenameToggle.IsOn = s.GitHubCheckRepoRename;
            GhReqPrefixBox.Text = s.GitHubReqPrefix ?? "";
            _loading = false;
        }

        private void Save() { if (!_loading) App.Settings.Save(); }

        private void PinUpdatesToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.PinUpdates = PinUpdatesToggle.IsOn; Save(); }
        private void BuryNonInstalledToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.BuryNonInstalled = BuryNonInstalledToggle.IsOn; Save(); }

        private void SortByCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortByCombo.SelectedIndex >= 0) { App.Settings.SortColumn = SortByCombo.SelectedIndex; Save(); }
        }

        private void SortOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortOrderCombo.SelectedIndex >= 0) { App.Settings.SortOrder = SortOrderCombo.SelectedIndex; Save(); }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ThemeCombo.SelectedItem is not ComboBoxItem item) return;
            App.Settings.Theme = (string)item.Tag;
            Save();
            if (App.MainWindowInstance?.Content is FrameworkElement root)
                root.RequestedTheme = App.Settings.Theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
        }

        private void UpdateIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || UpdateIntervalCombo.SelectedItem is not ComboBoxItem item) return;
            App.Settings.UpdateIntervalMinutes = (int)item.Tag;
            Save();
        }

        private void MinAgeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || MinAgeCombo.SelectedItem is not ComboBoxItem item) return;
            App.Settings.MinimumUpdateAgeDays = (int)item.Tag;
            Save();
        }

        private void CheckOnStartToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.CheckOnStart = CheckOnStartToggle.IsOn; Save(); }
        private void NotifyToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.NotifyUpdates = NotifyToggle.IsOn; Save(); }
        private void HideDowngradesToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.HideDowngrades = HideDowngradesToggle.IsOn; Save(); }
        private void AddWithoutCheckToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.AllowAddingAppsWithoutCheck = AddWithoutCheckToggle.IsOn; Save(); }
        private void RepoRenameToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.GitHubCheckRepoRename = RepoRenameToggle.IsOn; Save(); }

        private void GlobalVersionRegexBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalVersionExtractionRegEx = EmptyToNull(GlobalVersionRegexBox.Text); Save(); }
        private void GlobalMatchGroupBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalMatchGroupToUse = EmptyToNull(GlobalMatchGroupBox.Text); Save(); }
        private void GlobalAssetFilterBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalAssetFilterRegEx = EmptyToNull(GlobalAssetFilterBox.Text); Save(); }
        private void GitHubPatBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GitHubPersonalAccessToken = EmptyToNull(GitHubPatBox.Password); Save(); }
        private void GhReqPrefixBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GitHubReqPrefix = EmptyToNull(GhReqPrefixBox.Text); Save(); }

        private static string? EmptyToNull(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
