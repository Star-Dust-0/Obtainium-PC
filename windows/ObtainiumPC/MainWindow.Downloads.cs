using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ObtainiumPC.Services.Download;
using Loc = ObtainiumPC.Services.Localization.Loc;

namespace ObtainiumPC
{
    /// Global download strip, Downloads list flyout, taskbar progress and the
    /// close-while-downloading warning. All state comes from DownloadRegistry.
    public sealed partial class MainWindow
    {
        private Flyout? _downloadsFlyout;
        private bool _flyoutOpen;
        private bool _closeConfirmed;
        private bool _closeDialogOpen;

        private void InitDownloadCenter()
        {
            _downloadsFlyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight };
            _downloadsFlyout.Opening += (_, _) => { _flyoutOpen = true; BuildDownloadsFlyout(); };
            _downloadsFlyout.Closed += (_, _) => _flyoutOpen = false;
            StripListButton.Flyout = _downloadsFlyout;
            DownloadRegistry.ListChanged += () => DispatcherQueue.TryEnqueue(RefreshDownloadStrip);
            AppWindow.Closing += AppWindow_Closing;
        }

        private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

        private void RefreshDownloadStrip()
        {
            var active = DownloadRegistry.ActiveEntries();
            var finished = DownloadRegistry.FinishedEntries();
            DownloadStrip.Visibility = active.Count + finished.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var agg = DownloadRegistry.Aggregate();
            var (title, detail) = DownloadTexts.Strip(active, finished, agg.Received, agg.Total);
            StripTitle.Text = Loc.T(title);
            StripDetail.Text = detail;
            StripBar.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StripCancel.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StripCancel.Content = active.Count > 1 ? Loc.T("Cancel all") : Loc.T("Cancel");
            if (agg.Fraction is { } f) { StripBar.IsIndeterminate = false; StripBar.Value = f * 100; }
            else StripBar.IsIndeterminate = active.Count > 0;
            TaskbarProgress.Set(Hwnd, active.Count > 0, agg.Fraction);
            if (_flyoutOpen) BuildDownloadsFlyout();
        }

        private void StripCancel_Click(object sender, RoutedEventArgs e) => DownloadRegistry.CancelAll();

        private static Microsoft.UI.Xaml.Media.Brush DotBrush(DownloadState s) => new SolidColorBrush(s switch
        {
            DownloadState.Downloading => Windows.UI.Color.FromArgb(255, 0, 95, 184),
            DownloadState.InstallerOpened or DownloadState.Saved or DownloadState.InstallUnconfirmed => Windows.UI.Color.FromArgb(255, 157, 93, 0),
            DownloadState.Installed => Windows.UI.Color.FromArgb(255, 16, 124, 16),
            DownloadState.Failed => Windows.UI.Color.FromArgb(255, 196, 43, 28),
            _ => Windows.UI.Color.FromArgb(255, 96, 96, 96),
        });

        private void BuildDownloadsFlyout()
        {
            if (_downloadsFlyout == null) return;
            var list = new StackPanel { Spacing = 10, MinWidth = 380, MaxWidth = 460 };
            list.Children.Add(new TextBlock { Text = Loc.T("Downloads"), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var entry in DownloadRegistry.ActiveEntries().Concat(DownloadRegistry.FinishedEntries()))
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = DotBrush(entry.State), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = entry.AppName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = DownloadTexts.Status(entry), FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
                if (entry.State == DownloadState.Downloading)
                {
                    var (r, t) = entry.Snapshot;
                    var bar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 4, 0, 0) };
                    if (t is > 0) bar.Value = Math.Min(100, (double)r / t.Value * 100); else bar.IsIndeterminate = true;
                    text.Children.Add(bar);
                }
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                var captured = entry;
                Button? action = null;
                if (entry.State == DownloadState.Downloading)
                {
                    action = new Button { Content = Loc.T("Cancel"), IsEnabled = !entry.CancelRequested };
                    action.Click += (_, _) => captured.Cancel();
                }
                else if (entry.State is DownloadState.Failed or DownloadState.Cancelled)
                {
                    action = new Button { Content = Loc.T("Retry"), IsEnabled = entry.Asset != null };
                    action.Click += (_, _) => _ = DownloadCoordinator.RetryAsync(captured);
                }
                else if (entry.State == DownloadState.InstallUnconfirmed)
                {
                    action = new Button { Content = Loc.T("Check again") };
                    action.Click += (_, _) => DownloadCoordinator.CheckAgain(captured);
                }
                else if (entry.State is DownloadState.InstallerOpened or DownloadState.Installed)
                {
                    action = new Button { Content = Loc.T("Open app") };
                    action.Click += (_, _) => { _downloadsFlyout?.Hide(); NavigateToApp(captured.AppId); };
                }
                if (action != null) { Grid.SetColumn(action, 2); row.Children.Add(action); }
                list.Children.Add(row);
            }
            if (DownloadRegistry.FinishedEntries().Count > 0)
            {
                var clear = new HyperlinkButton { Content = Loc.T("Clear finished") };
                clear.Click += (_, _) => DownloadRegistry.ClearFinished();
                list.Children.Add(clear);
            }
            _downloadsFlyout.Content = new ScrollViewer { MaxHeight = 420, Content = list };
        }

        /// Window.Close() does not raise AppWindow.Closing, so the caption Close button asks here first.
        private void RequestClose()
        {
            if (_closeConfirmed || DownloadRegistry.ActiveCount == 0) { Close(); return; }
            _ = ConfirmCloseAsync();
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_closeConfirmed || DownloadRegistry.ActiveCount == 0) return;
            args.Cancel = true;
            _ = ConfirmCloseAsync();
        }

        private async System.Threading.Tasks.Task ConfirmCloseAsync()
        {
            if (_closeDialogOpen) return;
            _closeDialogOpen = true;
            try
            {
                var dlg = new ThemedContentDialog
                {
                    Title = Loc.T("A download is running"),
                    Content = Loc.T("Closing now cancels the running download and removes its partial file. Resuming is not supported, so it would start again from the beginning."),
                    PrimaryButtonText = Loc.T("Cancel downloads and close"),
                    CloseButtonText = Loc.T("Keep downloading"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
                DownloadRegistry.CancelAll();
                // Each job deletes its own .part as it unwinds; wait briefly so none is left behind.
                for (var i = 0; i < 40 && DownloadRegistry.ActiveCount > 0; i++) await System.Threading.Tasks.Task.Delay(100);
                _closeConfirmed = true;
                Close();
            }
            finally { _closeDialogOpen = false; }
        }
    }
}
