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

    public static ImageSource? Source(TrackedApp app)
    {
        var uri = UriFor(app.Url);
        return uri == null ? null : new BitmapImage(uri);
    }
}
