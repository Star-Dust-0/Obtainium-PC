using System.Net.Http;
using System.Text.Json;
using HtmlAgilityPack;

namespace ObtainiumPC.Sources.TelegramApp;

/// <summary>
/// Port of lib/app_sources/telegramapp.dart: the Telegram Android APK. The
/// version is scraped from the public TAndroidAPK channel page; the download
/// URL is fixed. Track-only on Windows.
/// </summary>
public sealed class TelegramAppSource : AppSourceBase
{
    private const string ChannelUrl = "https://t.me/s/TAndroidAPK";
    private const string DownloadUrl = "https://telegram.org/dl/android/apk";

    private readonly HttpClient _http;

    public TelegramAppSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Telegram App";
    public override IReadOnlyList<string> Hosts => new[] { "telegram.org" };
    public override bool SupportsInstall => false;

    // Telegram has a single known APK download page - the exact input URL
    // does not matter, so normalize to the homepage (as upstream).
    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        $"https://{Hosts[0]}";

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var res = await _http.GetAsync(ChannelUrl, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, ChannelUrl);

        var doc = new HtmlDocument();
        doc.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));
        var messages = doc.DocumentNode.SelectNodes("//*[contains(concat(' ', normalize-space(@class), ' '), ' tgme_widget_message_text ')]");
        var version = messages?.LastOrDefault()?.InnerHtml
            .Split('\n').FirstOrDefault()?.Trim().Split(' ').FirstOrDefault();
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();

        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new($"telegram-{version}.apk", DownloadUrl) },
            new AppNames("Telegram", "Telegram"),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
