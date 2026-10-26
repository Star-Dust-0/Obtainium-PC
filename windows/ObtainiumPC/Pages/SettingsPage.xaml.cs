using System;
using System.Collections.Generic;
using System.Linq;
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
            Loaded += (_, _) => { Services.Localization.Localizer.Apply(this); BuildSectionList(); ApplyLayout(); };
            LoadValues();
        }

        // Wide: section list on the left, one section on the right (Task Scheduler Studio
        // settings pattern). Narrow: every section stacked, no list.
        private bool _wideLayout;
        private double _splitWidth;
        private Border[] Sections => new[] { SecAppearance, SecUpdates, SecVersion, SecGitHub, SecCategories, SecAbout };

        private void BuildSectionList()
        {
            SectionList.Items.Clear();
            foreach (var card in Sections)
            {
                var heading = ((StackPanel)card.Child).Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
                SectionList.Items.Add(new ListViewItem { Content = new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap }, Tag = card });
            }
            SectionList.SelectedIndex = 0;
        }

        private void Split_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _splitWidth = e.NewSize.Width;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            var wide = _splitWidth >= 680;
            _wideLayout = wide;
            SectionNavColumn.Width = wide ? new GridLength(190) : new GridLength(0);
            SectionList.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            var avail = _splitWidth - (wide ? 190 : 0) - 8;
            SectionHost.Width = avail > 0 ? Math.Min(avail, 760) : double.NaN;
            ShowSelectedSection();
        }

        private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelectedSection();

        private void ShowSelectedSection()
        {
            var selected = (SectionList.SelectedItem as ListViewItem)?.Tag as Border;
            foreach (var card in Sections)
                card.Visibility = !_wideLayout || selected == null || ReferenceEquals(card, selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadValues()
        {
            ReloadCategoryList();
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
            DensityCombo.Items.Add("Standard");
            DensityCombo.Items.Add("Compact");
            DensityCombo.Items.Add("Dense");
            DensityCombo.SelectedIndex = App.Settings.ListDensity == "dense" ? 2 : App.Settings.ListDensity == "compact" ? 1 : 0;
            GroupByCombo.Items.Add("None");
            GroupByCombo.Items.Add("Category");
            GroupByCombo.Items.Add("Source");
            GroupByCombo.SelectedIndex = App.Settings.GroupBy == "category" ? 1 : App.Settings.GroupBy == "source" ? 2 : 0;
            CollapseGroupsToggle.IsOn = App.Settings.CollapseGroupsOnStartup;
            ThemeCombo.SelectedIndex = Math.Max(0, Array.FindIndex(ThemeOptions, o => o.Value == s.Theme));
            // Upstream _LocaleDropdown: "Follow system" + supportedLocales.
            LanguageCombo.Items.Add(new ComboBoxItem { Content = Services.Localization.Loc.T("Follow system"), Tag = "" });
            var langIdx = 0;
            var li = 1;
            foreach (var (tag, name) in Services.Localization.LocTranslations.SupportedLanguages)
            {
                LanguageCombo.Items.Add(new ComboBoxItem { Content = name, Tag = tag });
                if (App.Settings.Language == tag) langIdx = li;
                li++;
            }
            LanguageCombo.SelectedIndex = langIdx;

            foreach (var (label, minutes) in IntervalOptions) UpdateIntervalCombo.Items.Add(new ComboBoxItem { Content = label, Tag = minutes });
            var ii = Array.FindIndex(IntervalOptions, o => o.Minutes == s.UpdateIntervalMinutes);
            UpdateIntervalCombo.SelectedIndex = ii >= 0 ? ii : 3;

            foreach (var d in MinAgeOptions) MinAgeCombo.Items.Add(new ComboBoxItem { Content = d == 0 ? "None" : d == 1 ? "1 day" : $"{d} days", Tag = d });
            var mi = Array.IndexOf(MinAgeOptions, s.MinimumUpdateAgeDays);
            MinAgeCombo.SelectedIndex = mi >= 0 ? mi : 0;

            CheckOnStartToggle.IsOn = s.CheckOnStart;
            HandleLinksToggle.IsOn = s.HandleObtainiumLinks;
            NotifyToggle.IsOn = s.NotifyUpdates;
            HideDowngradesToggle.IsOn = s.HideDowngrades;
            CheckUpdateOnDetailToggle.IsOn = s.CheckUpdateOnDetailPage;
            OnlyCheckInstalledToggle.IsOn = s.OnlyCheckInstalledOrTrackOnlyApps;
            PrereleasesDefaultToggle.IsOn = s.IncludePrereleasesByDefault;
            HideTrackOnlyWarningToggle.IsOn = s.HideTrackOnlyWarning;
            ShowDowngradeErrorToggle.IsOn = s.ShowAppDowngradeError;
            SkipBulkConfirmToggle.IsOn = s.SkipBulkUpdateConfirmation;
            ParallelDownloadsToggle.IsOn = s.ParallelDownloads;
            CertPinningToggle.IsOn = s.EnableCertificatePinning;
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

        private void GroupByCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            App.Settings.GroupBy = GroupByCombo.SelectedIndex == 1 ? "category" : GroupByCombo.SelectedIndex == 2 ? "source" : "none";
            Save();
        }

        private void CollapseGroupsToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.CollapseGroupsOnStartup = CollapseGroupsToggle.IsOn; Save(); }

        private void SortOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortOrderCombo.SelectedIndex >= 0) { App.Settings.SortOrder = SortOrderCombo.SelectedIndex; Save(); }
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem item) return;
            var lang = (string)item.Tag;
            if (App.Settings.Language == lang) return;
            App.Settings.Language = lang;
            Save();
            Services.Localization.Loc.SetLanguage(lang);
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
        private void HandleLinksToggle_Toggled(object sender, RoutedEventArgs e)
        {
            App.Settings.HandleObtainiumLinks = HandleLinksToggle.IsOn;
            Save();
            Services.DeepLinkService.ApplyRegistrationSetting();
        }
        private void NotifyToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.NotifyUpdates = NotifyToggle.IsOn; Save(); }
        private void HideDowngradesToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.HideDowngrades = HideDowngradesToggle.IsOn; Save(); }
        private void CheckUpdateOnDetailToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.CheckUpdateOnDetailPage = CheckUpdateOnDetailToggle.IsOn; Save(); }
        private void OnlyCheckInstalledToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.OnlyCheckInstalledOrTrackOnlyApps = OnlyCheckInstalledToggle.IsOn; Save(); }
        private void PrereleasesDefaultToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.IncludePrereleasesByDefault = PrereleasesDefaultToggle.IsOn; Save(); }
        private void HideTrackOnlyWarningToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.HideTrackOnlyWarning = HideTrackOnlyWarningToggle.IsOn; Save(); }
        private void ShowDowngradeErrorToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.ShowAppDowngradeError = ShowDowngradeErrorToggle.IsOn; Save(); }
        private void SkipBulkConfirmToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.SkipBulkUpdateConfirmation = SkipBulkConfirmToggle.IsOn; Save(); }
        private void ParallelDownloadsToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.ParallelDownloads = ParallelDownloadsToggle.IsOn; Save(); }
        private void CertPinningToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.EnableCertificatePinning = CertPinningToggle.IsOn; Save(); }
        private void AddWithoutCheckToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.AllowAddingAppsWithoutCheck = AddWithoutCheckToggle.IsOn; Save(); }
        public sealed class CategoryRow
        {
            public string Name { get; set; } = "";
            public Microsoft.UI.Xaml.Media.Brush Swatch { get; set; } = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        private void ReloadCategoryList()
        {
            CategoryList.ItemsSource = App.Settings.CategoryColors
                .OrderBy(kv => kv.Key)
                .Select(kv => new CategoryRow
                {
                    Name = kv.Key,
                    Swatch = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, (byte)(kv.Value >> 16), (byte)(kv.Value >> 8), (byte)kv.Value)),
                })
                .ToList();
        }

        // lib/providers/settings_provider.dart setCategories: deleting a
        // category also removes it from every app.
        private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string name) return;
            App.Settings.CategoryColors.Remove(name);
            App.Settings.Save();
            foreach (var a in App.TrackedApps.Where(a => a.Categories.Remove(name))) { }
            await App.SaveAppsAsync();
            ReloadCategoryList();
        }

        private void DensityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            App.Settings.ListDensity = DensityCombo.SelectedIndex == 2 ? "dense" : DensityCombo.SelectedIndex == 1 ? "compact" : "standard";
            Save();
        }
        private void RepoRenameToggle_Toggled(object sender, RoutedEventArgs e) { App.Settings.GitHubCheckRepoRename = RepoRenameToggle.IsOn; Save(); }

        private void GlobalVersionRegexBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalVersionExtractionRegEx = EmptyToNull(GlobalVersionRegexBox.Text); Save(); }
        private void GlobalMatchGroupBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalMatchGroupToUse = EmptyToNull(GlobalMatchGroupBox.Text); Save(); }
        private void GlobalAssetFilterBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GlobalAssetFilterRegEx = EmptyToNull(GlobalAssetFilterBox.Text); Save(); }
        private void GitHubPatBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GitHubPersonalAccessToken = EmptyToNull(GitHubPatBox.Password); Save(); }
        private void GhReqPrefixBox_LostFocus(object sender, RoutedEventArgs e) { App.Settings.GitHubReqPrefix = EmptyToNull(GhReqPrefixBox.Text); Save(); }

        private static string? EmptyToNull(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
