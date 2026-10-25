using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.Apk4Free;

/// <summary>
/// Port of lib/app_sources/apk4free.dart: two-page scrape (store page then
/// download page), with the same title-cleaning and version-fallback chain.
/// Track-only on Windows.
/// </summary>
public sealed class Apk4FreeSource : AppSourceBase
{
    private static readonly Regex VersionRegex = new(@"v?(\d+(\.\d+)+)", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public Apk4FreeSource(HttpClient? http = null)
    {
        _http = http ?? ObtainiumHttp.CreateClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Apk4Free";
    public override IReadOnlyList<string> Hosts => new[] { "apk4free.net" };
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/[^/]+/?");

    private static string CleanTitle(string title)
    {
        var cleaned = Regex.Replace(title, @"\[.*?\]|\{.*?\}", "");
        cleaned = Regex.Replace(cleaned, @"\b(APK|MOD|XAPK|HACK)\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned,
            @"\((?:[^)]*?(?:Unlocked|Mod|Premium|Money|Menu|Full|Patched|Subscribed|AdFree|BG Play|Paid|Unlimited|God Mode)[^)]*?)\)",
            "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+v?\d+(\.\d+)+.*$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+[\+\-]\s+", " ");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var res = await _http.GetAsync(standardUrl, cancellationToken);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var html = new HtmlDocument();
        html.LoadHtml(await res.Content.ReadAsStringAsync(cancellationToken));

        var titleElement = html.DocumentNode.SelectSingleNode("//h1[contains(@class,'main-box-title')]");
        var rawTitle = titleElement?.InnerText.Trim() ?? "";
        var appName = string.IsNullOrEmpty(rawTitle) ? standardUrl.Split('/').Last() : CleanTitle(rawTitle);

        var appVersion = html.DocumentNode.SelectSingleNode("//div[contains(@class,'version')]")?.InnerText.Trim();
        if (string.IsNullOrEmpty(appVersion)) appVersion = null;
        if (appVersion == null)
        {
            var match = VersionRegex.Match(rawTitle);
            if (match.Success) appVersion = match.Groups[1].Value;
        }

        var downloadPageLink = (html.DocumentNode.SelectNodes("//a[contains(@class,'downloadAPK')]")
                ?? Enumerable.Empty<HtmlNode>())
            .Select(a => a.GetAttributeValue("href", ""))
            .FirstOrDefault(h => h.Contains("/download/"));
        if (string.IsNullOrEmpty(downloadPageLink))
        {
            downloadPageLink = (html.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
                .Select(a => a.GetAttributeValue("href", ""))
                .FirstOrDefault(h => h.Contains("/download/"));
        }
        if (string.IsNullOrEmpty(downloadPageLink)) throw new NoReleasesException();

        var resDlPage = await _http.GetAsync(downloadPageLink, cancellationToken);
        if ((int)resDlPage.StatusCode >= 400) throw new ObtainiumHttpException((int)resDlPage.StatusCode, downloadPageLink);
        var htmlDlPage = new HtmlDocument();
        htmlDlPage.LoadHtml(await resDlPage.Content.ReadAsStringAsync(cancellationToken));

        var apkUrls = new List<KeyValuePair<string, string>>();
        foreach (var btn in htmlDlPage.DocumentNode.SelectNodes("//a[contains(@class,'downloadAPK')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = btn.GetAttributeValue("href", null);
            if (href != null) apkUrls.Add(new KeyValuePair<string, string>(btn.InnerText.Trim(), href.Trim()));
        }
        if (apkUrls.Count == 0)
        {
            foreach (var link in htmlDlPage.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = link.GetAttributeValue("href", null)?.Trim();
                if (href == null || !AssetFilterService.IsAppOrContainerFile(href)) continue;
                var linkText = link.InnerText.Trim();
                if (linkText.Length == 0) linkText = href.Split('/').Last();
                apkUrls.Add(new KeyValuePair<string, string>(linkText, href));
            }
        }
        if (apkUrls.Count == 0) throw new NoAppAssetException();

        if (appVersion == null)
        {
            foreach (var entry in apkUrls)
            {
                var match = VersionRegex.Match(entry.Key);
                if (!match.Success) match = VersionRegex.Match(entry.Value);
                if (match.Success) { appVersion = match.Groups[1].Value; break; }
            }
        }
        if (appVersion == null) throw new FDroid.NoVersionException();

        return new AssetDetails(
            appVersion.Trim(),
            apkUrls,
            new AppNames(Name, appName),
            null,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
