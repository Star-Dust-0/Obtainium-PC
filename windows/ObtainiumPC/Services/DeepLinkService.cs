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
/// as a command-line argument (shell open command passes %1).
/// </summary>
public static class DeepLinkService
{
    private const string Scheme = "obtainium";
    private const string ClassesKey = @"Software\Classes\" + Scheme;

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command");
        return key != null;
    }

    public static void ApplyRegistrationSetting()
    {
        try
        {
            if (App.Settings.HandleObtainiumLinks && !IsRegistered()) Register();
            else if (!App.Settings.HandleObtainiumLinks && IsRegistered()) Unregister();
        }
        catch (Exception ex)
        {
            Logging.AppLogger.Warn("obtainium:// protocol registration failed", ex);
        }
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
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

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(ClassesKey, throwOnMissingSubKey: false);

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

    private static async Task ShowErrorAsync(MainWindow mw, string message)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.T("Import failed"),
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
        catch { await ShowErrorAsync(mw, Loc.T("Invalid link")); return; }
        var action = uri.Host.ToLowerInvariant();
        var query = ParseQuery(uri.Query);
        query.TryGetValue("url", out var qurl);
        var data = !string.IsNullOrEmpty(qurl)
            ? qurl!
            : uri.AbsolutePath.Length > 1 ? Uri.UnescapeDataString(uri.AbsolutePath.Substring(1)) : "";

        switch (action)
        {
            case "add":
            {
                // Upstream: standardize when the URL resolves to a source, then
                // open the existing app on a standardized-or-raw URL match;
                // otherwise open Add App prefilled.
                string? standardized = null;
                try
                {
                    if (Sources.SourceCatalog.TryResolve(data, App.Settings, out var src) && src != null)
                        standardized = src.SourceSpecificStandardizeUrl(data);
                }
                catch { standardized = null; }
                var existing = App.TrackedApps.FirstOrDefault(a => a.Url == standardized || a.Url == data);
                if (existing != null) mw.NavigateToApp(existing.Id);
                else mw.NavigateToAddApp(data);
                break;
            }
            case "app":
            case "apps":
            {
                // Upstream: confirmation dialog showing the raw JSON in an
                // expansion tile, then jsonEncode({'apps': action == 'app' ?
                // [parsedData] : parsedData}) through the normal import.
                var expander = new Expander
                {
                    Header = Loc.T("Raw JSON"),
                    Content = new ScrollViewer
                    {
                        MaxHeight = 300,
                        Content = new TextBlock
                        {
                            Text = data,
                            FontFamily = new FontFamily("Consolas"),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true,
                        },
                    },
                };
                var dialog = new ContentDialog
                {
                    Title = Loc.T(action == "app" ? "Import app" : "Import apps"),
                    Content = new ScrollViewer { Content = expander, MaxHeight = 340, Width = 560 },
                    PrimaryButtonText = Loc.T("Import"),
                    CloseButtonText = Loc.T("Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = mw.Content.XamlRoot,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                string payload;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(data);
                    payload = action == "app"
                        ? "{\"apps\":[" + doc.RootElement.GetRawText() + "]}"
                        : "{\"apps\":" + doc.RootElement.GetRawText() + "}";
                }
                catch (System.Text.Json.JsonException)
                {
                    await ShowErrorAsync(mw, Loc.T("Invalid input"));
                    return;
                }
                try
                {
                    var (apps, _, _) = Store.AppStore.Import(payload);
                    var added = 0;
                    var replaced = 0;
                    foreach (var app in apps)
                    {
                        var i = App.TrackedApps.FindIndex(x => x.Id == app.Id);
                        if (i >= 0) { App.TrackedApps[i] = app; replaced++; }
                        else { App.TrackedApps.Add(app); added++; }
                    }
                    await App.SaveAppsAsync();
                    var done = new ContentDialog
                    {
                        Title = Loc.T("Import complete"),
                        Content = $"{added} app(s) added, {replaced} replaced.",
                        CloseButtonText = Loc.T("Close"),
                        XamlRoot = mw.Content.XamlRoot,
                    };
                    await done.ShowAsync();
                    mw.NavigateToApps();
                }
                catch (Exception ex)
                {
                    await ShowErrorAsync(mw, ex.Message);
                }
                break;
            }
            case "refresh":
            {
                // Upstream: checkUpdates(forceAll: id == null, specificIds: [id]).
                query.TryGetValue("id", out var id);
                if (string.IsNullOrEmpty(id))
                {
                    _ = Updates.BackgroundUpdateService.Instance.CheckAllAsync(manual: true);
                }
                else
                {
                    var app = App.TrackedApps.FirstOrDefault(a => a.Id == id);
                    if (app != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await App.Updater.CheckAppAsync(app.Url, app.AdditionalSettings, app);
                                await App.SaveAppsAsync();
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
                await ShowErrorAsync(mw, Loc.T("Unknown link"));
                break;
        }
    }
}
