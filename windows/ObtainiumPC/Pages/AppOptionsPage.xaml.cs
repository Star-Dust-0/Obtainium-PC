using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ObtainiumPC.Models;
using ObtainiumPC.Sources;
using ObtainiumPC.Sources.Forms;

namespace ObtainiumPC.Pages
{
    /// <summary>
    /// Per-app "Additional options" page. Renders the source's combined form spec
    /// (ported from lib/app_sources/app_source.dart + update_health.dart) and
    /// persists each change into the app's additionalSettings immediately, as the
    /// original dialog does.
    /// </summary>
    public sealed partial class AppOptionsPage : Page
    {
        private TrackedApp? _app;
        private bool _building;

        public AppOptionsPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await App.InitAsync();
            var id = e.Parameter as string;
            _app = App.TrackedApps.FirstOrDefault(a => a.Id == id);
            if (_app == null) { Frame.GoBack(); return; }
            TitleText.Text = _app.FinalName + " options";
            BuildForm();
        }

        private void BuildForm()
        {
            if (_app == null) return;
            _building = true;
            FormPanel.Children.Clear();

            if (!SourceCatalog.TryResolve(_app.Url, App.Settings, out var source) || source == null)
            {
                FormPanel.Children.Add(new TextBlock
                {
                    Text = "This app's source is not supported yet, so its options cannot be shown.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
                _building = false;
                return;
            }

            var rows = AppOptionFormSpec.Build(source);
            var stored = _app.AdditionalSettings;

            foreach (var row in rows)
            foreach (var item in row)
            {
                switch (item.Kind)
                {
                    case OptionFormItemKind.Header:
                        FormPanel.Children.Add(new TextBlock
                        {
                            Text = item.Label,
                            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"],
                            Margin = new Thickness(0, 20, 0, 6),
                        });
                        break;

                    case OptionFormItemKind.Switch:
                    {
                        var initial = stored.TryGetValue(item.Key, out var sv) && sv.ValueKind is JsonValueKind.True or JsonValueKind.False
                            ? sv.GetBoolean() : item.DefaultBool;
                        if (item.Disabled) initial = false; // disabled items are forced off, as upstream does
                        var toggle = new ToggleSwitch
                        {
                            Header = item.Label,
                            IsOn = initial,
                            IsEnabled = !item.Disabled,
                            Margin = new Thickness(0, 4, 0, 4),
                        };
                        var key = item.Key;
                        toggle.Toggled += async (_, _) =>
                        {
                            if (_building || _app == null) return;
                            _app.SetSetting(key, toggle.IsOn);
                            await App.SaveAppsAsync();
                        };
                        FormPanel.Children.Add(toggle);
                        break;
                    }

                    case OptionFormItemKind.Text:
                    {
                        var initial = stored.TryGetValue(item.Key, out var sv) && sv.ValueKind == JsonValueKind.String
                            ? sv.GetString() ?? "" : item.DefaultString ?? "";
                        var box = new TextBox
                        {
                            Header = item.Label,
                            Text = initial,
                            PlaceholderText = item.Hint ?? "",
                            TextWrapping = TextWrapping.Wrap,
                        };
                        var error = new TextBlock
                        {
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"],
                            FontSize = 12,
                            Visibility = Visibility.Collapsed,
                            Margin = new Thickness(0, 2, 0, 6),
                        };
                        var key = item.Key;
                        var validateRegex = item.ValidateRegex;
                        box.LostFocus += async (_, _) =>
                        {
                            if (_building || _app == null) return;
                            var value = box.Text.Trim();
                            if (validateRegex && value.Length > 0)
                            {
                                try { _ = Regex.Match("", value); error.Visibility = Visibility.Collapsed; }
                                catch (ArgumentException)
                                {
                                    error.Text = "Invalid regular expression; not saved.";
                                    error.Visibility = Visibility.Visible;
                                    return;
                                }
                            }
                            error.Visibility = Visibility.Collapsed;
                            _app.SetSetting(key, value.Length == 0 ? null : value);
                            await App.SaveAppsAsync();
                        };
                        FormPanel.Children.Add(box);
                        FormPanel.Children.Add(error);
                        break;
                    }

                    case OptionFormItemKind.ChoiceList:
                    {
                        var current = stored.TryGetValue(item.Key, out var sv) && sv.ValueKind == JsonValueKind.String
                            ? sv.GetString() : null;
                        current ??= item.DefaultString ?? "";
                        var combo = new ComboBox { Header = item.Label, MinWidth = 260, Margin = new Thickness(0, 4, 0, 8) };
                        var values = new List<string>();
                        var selectedIndex = 0;
                        for (var i = 0; i < item.Options.Count; i++)
                        {
                            var opt = item.Options[i];
                            values.Add(opt.Key);
                            combo.Items.Add(new ComboBoxItem { Content = opt.Value, Tag = opt.Key });
                            if (opt.Key == current) selectedIndex = i;
                        }
                        combo.SelectedIndex = selectedIndex;
                        var key = item.Key;
                        combo.SelectionChanged += async (_, _) =>
                        {
                            if (_building || _app == null || combo.SelectedIndex < 0) return;
                            _app.SetSetting(key, values[combo.SelectedIndex]);
                            await App.SaveAppsAsync();
                        };
                        FormPanel.Children.Add(combo);
                        break;
                    }
                }
            }
            _building = false;
        }
    }
}
