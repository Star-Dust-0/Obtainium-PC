using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ObtainiumPC.Models;

namespace ObtainiumPC.Services;

/// Remote app icon: the repository owner's avatar for GitHub-hosted apps.
/// Other sources fall back to the placeholder glyph (the avatar is a stand-in,
/// not the app's own icon). Failures leave the placeholder in place.
public static class AppIconService
{
    public static Uri? UriFor(string? appUrl)
    {
        if (string.IsNullOrWhiteSpace(appUrl)) return null;
        if (!Uri.TryCreate(appUrl, UriKind.Absolute, out var u)) return null;
        if (!u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && !u.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return null;
        return new Uri("https://github.com/" + Uri.EscapeDataString(parts[0]) + ".png?size=96");
    }

    private static IReadOnlyList<ObtainiumPC.Services.Detection.InstalledAppInfo>? _installed;
    private static DateTime _installedAt;

    private static IReadOnlyList<ObtainiumPC.Services.Detection.InstalledAppInfo> Installed()
    {
        if (_installed == null || DateTime.UtcNow - _installedAt > TimeSpan.FromMinutes(2))
        {
            _installed = ObtainiumPC.Services.Detection.InstalledAppsService.ListInstalledApps();
            _installedAt = DateTime.UtcNow;
        }
        return _installed;
    }

    /// Path of the installed program's own icon (registry DisplayIcon, else the exe in the
    /// install folder), only for Exact/Strong identity matches of an installed app.
    public static string? InstalledIconPath(TrackedApp app)
    {
        if (app.InstalledVersion == null) return null;
        var m = ObtainiumPC.Services.Detection.InstalledAppsService.FindMatch(app.FinalName, app.Author, Installed());
        if (m == null || m.Confidence < ObtainiumPC.Services.Detection.InstalledAppsService.MatchConfidence.Strong) return null;
        var icon = m.Info.DisplayIcon?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(icon))
        {
            var comma = icon.LastIndexOf(',');
            if (comma > 1 && int.TryParse(icon[(comma + 1)..], out _)) icon = icon[..comma].Trim('"');
            if (File.Exists(icon)) return icon;
        }
        return null;
    }

    /// The installed app's own icon when available (loaded from the exe via the shell),
    /// otherwise the GitHub owner avatar as a stand-in, otherwise nothing (placeholder glyph).
    public static ImageSource? Source(TrackedApp app)
    {
        try
        {
            var path = InstalledIconPath(app);
            if (path != null)
            {
                var bmp = new BitmapImage();
                _ = LoadFromFileAsync(bmp, path);
                return bmp;
            }
        }
        catch { /* fall back to the avatar */ }
        var uri = UriFor(app.Url);
        return uri == null ? null : new BitmapImage(uri);
    }

    private static readonly HashSet<string> _logged = new();

    private static async Task LoadFromFileAsync(BitmapImage bmp, string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 96);
            if (thumb != null)
            {
                // Diagnostic (once per path): which thumbnail kind the shell returned, to tell a
                // real exe icon from a generic/stand-in one when a capture shows a different icon.
                if (_logged.Add(path))
                    Logging.AppLogger.Info($"icon diag: {path} type={thumb.Type} size={thumb.OriginalWidth}x{thumb.OriginalHeight} culture={System.Globalization.CultureInfo.CurrentUICulture.Name}");
                await bmp.SetSourceAsync(thumb);
            }
        }
        catch (Exception e) { Logging.AppLogger.Warn("icon load failed: " + path, e); }
    }
}
