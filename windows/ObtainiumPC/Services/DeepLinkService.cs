using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using ObtainiumPC.Services.Localization;

namespace ObtainiumPC.Services;

/// <summary>
/// lib/pages/home.dart interpretLink: the obtainium:// deep link. On Android
/// the scheme is declared in the manifest; an unpackaged Windows exe cannot
/// declare activations, so it registers the scheme under HKCU\Software\Classes
/// (no admin needed, fully reversible from Settings) and receives link clicks
/// as a command-line argument (shell open command passes %1). Registration is
/// ownership-safe: another application's handler is never overwritten or
/// removed. Payloads are bounded, validated before confirmation, and only add
/// or replace tracking entries after the user confirms - never settings, and
/// never an install.
/// </summary>
public static class DeepLinkService
{
    private const string Scheme = "obtainium";
    private const string ClassesKey = @"Software\Classes\" + Scheme;

    /// Hard bound on a deep-link payload; legitimate app payloads are a few KB.
    private const int MaxPayloadChars = 1_000_000;

    private static string ExePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    private static string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command");
        return key?.GetValue("") as string;
    }

    /// Anyone's handler present?
    public static bool IsRegistered() => RegisteredCommand() != null;

    /// Ownership check: the open command points at this app. A moved exe
    /// still counts as ours via the product name, so re-registration can
    /// repair the path; another application's handler is never touched.
    public static bool IsRegisteredByUs()
    {
        var cmd = RegisteredCommand();
        return cmd != null
            && (cmd.Contains(ExePath(), StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("ObtainiumPC", StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplyRegistrationSetting()
    {
        try
        {
            if (App.Settings.HandleObtainiumLinks)
            {
                if (!IsRegistered()) Register();
                else if (!IsRegisteredByUs())
                    Logging.AppLogger.Warn(
                        "obtainium:// is already registered to another application; leaving the existing handler untouched");
                else if (!RegisteredCommand()!.Contains(ExePath(), StringComparison.OrdinalIgnoreCase))
                    Register(); // our own registration, stale exe path: repair it
            }
            else if (IsRegisteredByUs())
            {
                Unregister();
            }
        }
        catch (Exception ex)
        {
            Logging.AppLogger.Warn("obtainium:// protocol registration failed", ex);
        }
    }

    public static void Register()
    {
        var exe = ExePath();
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesKey))
        {
            key.SetValue("", "URL:Obtainium");
            key.SetValue("URL Protocol", "");
        }
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesKey + @"\DefaultIcon"))
            key.SetValue("", $"\"{exe}\",0");
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesKey + @"\shell\open\command"))
            key.SetValue("", $"\"{exe}\" \"%1\"");
    }

    /// Removes only our own registration; never another application's.
    public static void Unregister()
    {
        if (!IsRegisteredByUs()) return;
        Registry.CurrentUser.DeleteSubKeyTree(ClassesKey, throwOnMissingSubKey: false);
    }

    /// Extracts an obtainium:// URL from a shell-open command line.
    public static bool TryExtractLink(string? commandLine, out string link)
    {
        link = "";
        if (string.IsNullOrEmpty(commandLine)) return false;
        var i = commandLine.IndexOf("obtainium://", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;
        var rest = commandLine[i..];
        var end = rest.IndexOfAny(new[] { ' ', '\t', '"' });
        link = (end < 0 ? rest : rest[..end]).Trim().Trim('"');
        return link.Length > "obtainium://".Length;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0) dict[System.Net.WebUtility.UrlDecode(pair)] = "";
            else dict[System.Net.WebUtility.UrlDecode(pair[..idx])] = System.Net.WebUtility.UrlDecode(pair[(idx + 1)..]);
        }
        return dict;
    }

    private static async Task ShowErrorAsync(MainWindow mw, string title, string message)
    {
        var dialog = new ThemedContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = Loc.T("Close"),
            XamlRoot = mw.Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// Port of home.dart interpretLink: add | app | apps | refresh actions.
    public static async Task InterpretAsync(string link, MainWindow mw)
    {
        await App.InitAsync();
        Uri uri;
        try { uri = new Uri(link); }
        catch { await ShowErrorAsync(mw, Loc.T("Import failed"), Loc.T("Invalid link")); return; }
        var action = uri.Host.ToLowerInvariant();
        var query = ParseQuery(uri.Query);
        query.TryGetValue("url", out var qurl);
        var data = !string.IsNullOrEmpty(qurl)
            ? qurl!
            : uri.AbsolutePath.Length > 1 ? Uri.UnescapeDataString(uri.AbsolutePath.Substring(1)) : "";
        if (data.Length > MaxPayloadChars)
        {
            await ShowErrorAsync(mw, Loc.T("Import failed"), Loc.T("Invalid input"));
            return;
        }

        switch (action)
        {
            case "add":
            {
                // Upstream: standardize when the URL resolves to a source, then
                // open the existing app on a standardized-or-raw URL match;
                // otherwise open Add App prefilled. No mutation either way.
                string? standardized = null;
                try
                {
                    if (Sources.SourceCatalog.TryResolve(data, App.Settings, out var src) && src != null)
                        standardized = src.SourceSpecificStandardizeUrl(data);
                }
                catch { standardized = null; }
                var existing = App.SnapshotApps().FirstOrDefault(a => a.Url == standardized || a.Url == data);
                if (existing != null) mw.NavigateToApp(existing.Id);
                else mw.NavigateToAddApp(data);
                break;
            }
            case "app":
            case "apps":
            {
                // Validate BEFORE asking: parse the payload and run the import
                // parser dry so the confirmation can state exactly what will
                // happen. Malformed input never reaches the store.
                string payload;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(data);
                    if (action == "apps" && doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        await ShowErrorAsync(mw, Loc.T("Import failed"), Loc.T("Invalid input"));
                        return;
                    }
                    payload = action == "app"
                        ? "{\"apps\":[" + doc.RootElement.GetRawText() + "]}"
                        : "{\"apps\":" + doc.RootElement.GetRawText() + "}";
                }
                catch (System.Text.Json.JsonException)
                {
                    await ShowErrorAsync(mw, Loc.T("Import failed"), Loc.T("Invalid input"));
                    return;
                }
                List<Models.TrackedApp> apps;
                try
                {
                    (apps, _, _) = Store.AppStore.Import(payload);
                }
                catch (Exception ex)
                {
                    Logging.AppLogger.Error("Deep-link import failed", ex);
                    await ShowErrorAsync(mw, Loc.T("Import failed"),
                        ex is Store.ObtainiumImportException ? ex.Message : Loc.T("The request failed. Details are in the logs."));
                    return;
                }
                var skippedAndroid = apps.Where(a => App.Updater.IsLegacyUnsupported(a)).ToList();
                apps = apps.Except(skippedAndroid).ToList();
                var replaceCount = apps.Count(a => App.SnapshotApps().Any(x => x.Id == a.Id));

                // Upstream: confirmation dialog showing the raw JSON in an
                // expansion tile. This port additionally states the counts and
                // how many entries will be replaced before the user confirms.
                var expander = new Expander
                {
                    Header = Loc.T("Raw JSON"),
                    Content = new ScrollViewer
                    {
                        MaxHeight = 280,
                        Content = new TextBlock
                        {
                            Text = data,
                            FontFamily = new FontFamily("Consolas"),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true,
                        },
                    },
                };
                var summary = new TextBlock
                {
                    Text = string.Format(Loc.T("Apps to import: {0}. Will replace existing entries: {1}."), apps.Count, replaceCount)
                        + (skippedAndroid.Count > 0
                            ? "\n" + string.Format(Loc.T("Skipped {0} Android apps (not supported): {1}"), skippedAndroid.Count, string.Join(", ", skippedAndroid.Select(a => a.FinalName)))
                            : ""),
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap,
                };
                var panel = new StackPanel();
                panel.Children.Add(summary);
                panel.Children.Add(expander);
                var dialog = new ThemedContentDialog
                {
                    Title = Loc.T(action == "app" ? "Import app" : "Import apps"),
                    Content = new ScrollViewer { Content = panel, MaxHeight = 360, Width = 560 },
                    PrimaryButtonText = Loc.T("Import"),
                    CloseButtonText = Loc.T("Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = mw.Content.XamlRoot,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

                // Confirmed: tracking entries only. Imports via deep link carry
                // apps, never settings, and never trigger an install.
                var added = 0;
                var replaced = 0;
                foreach (var app in apps)
                {
                    if (App.UpsertApp(app)) replaced++; else added++;
                }
                await App.SaveAppsAsync();
                // Reload any open Apps page; the import bypasses update checks,
                // which are the usual AppsUpdated source.
                Updates.BackgroundUpdateService.Instance.NotifyAppsChanged();
                var done = new ThemedContentDialog
                {
                    Title = Loc.T("Import complete"),
                    Content = string.Format(Loc.T("Added: {0}; replaced: {1}."), added, replaced),
                    CloseButtonText = Loc.T("Close"),
                    XamlRoot = mw.Content.XamlRoot,
                };
                await done.ShowAsync();
                mw.NavigateToApps();
                break;
            }
            case "refresh":
            {
                // Upstream: checkUpdates(forceAll: id == null, specificIds: [id]).
                // Update checks only; installs stay interactive elsewhere.
                query.TryGetValue("id", out var id);
                if (string.IsNullOrEmpty(id))
                {
                    _ = Updates.BackgroundUpdateService.Instance.CheckAllAsync(manual: true);
                }
                else
                {
                    var app = App.SnapshotApps().FirstOrDefault(a => a.Id == id);
                    if (app != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await App.Updater.CheckAppAsync(app.Url, app.AdditionalSettings, app);
                                await App.SaveAppsAsync();
                                // Subscribers reload UI collections; marshal.
                                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                                    Updates.BackgroundUpdateService.Instance.NotifyAppsChanged());
                            }
                            catch (Exception ex)
                            {
                                Logging.AppLogger.Warn("Deep-link refresh failed", ex);
                            }
                        });
                    }
                }
                mw.NavigateToApps();
                break;
            }
            default:
                await ShowErrorAsync(mw, Loc.T("Import failed"), Loc.T("Unknown link"));
                break;
        }
    }
}
