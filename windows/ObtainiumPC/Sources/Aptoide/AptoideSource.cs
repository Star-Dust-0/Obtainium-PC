using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ObtainiumPC.Sources.Aptoide;

/// <summary>
/// Port of lib/app_sources/aptoide.dart: app ID is scraped from the store
/// page, details come from the ws2 API. Track-only on Windows.
/// </summary>
public sealed class AptoideSource : AppSourceBase
{
    private const string ApiBaseUrl = "https://ws2.aptoide.com/api/7/getApp/app_id";

    private readonly HttpClient _http;

    public AptoideSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
    }

    public override string Name => "Aptoide";
    public override IReadOnlyList<string> Hosts => new[] { "aptoide.com" };
    public override bool NaiveStandardVersionDetection => true;
    public override bool SupportsInstall => false;

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"([^\.]+\.)+", pathPattern: "");

    private async Task<JsonElement> GetAppDetailsJson(string standardUrl, CancellationToken ct)
    {
        var res = await _http.GetAsync(standardUrl, ct);
        if ((int)res.StatusCode >= 400) throw new ObtainiumHttpException((int)res.StatusCode, standardUrl);
        var idMatch = Regex.Match(await res.Content.ReadAsStringAsync(ct), "\"app\"\\s*:\\s*\\{\\s*\"id\"\\s*:\\s*([0-9]+)");
        if (!idMatch.Success) throw new NoReleasesException();

        var res2 = await _http.GetAsync($"{ApiBaseUrl}/{idMatch.Groups[1].Value}", ct);
        if ((int)res2.StatusCode >= 400) throw new ObtainiumHttpException((int)res2.StatusCode, standardUrl);
        using var doc = JsonDocument.Parse(await res2.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("nodes", out var nodes)
            || !nodes.TryGetProperty("meta", out var meta)
            || !meta.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
            throw new NoReleasesException();
        return data.Clone();
    }

    public override async Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var appDetails = await GetAppDetailsJson(standardUrl, cancellationToken);

        var appName = appDetails.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "App";
        var author = appDetails.TryGetProperty("developer", out var dev) && dev.ValueKind == JsonValueKind.Object
            && dev.TryGetProperty("name", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString()! : Name;

        string? version = null, apkUrl = null;
        if (appDetails.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.Object)
        {
            version = file.TryGetProperty("vername", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            apkUrl = file.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        }
        if (string.IsNullOrEmpty(version)) throw new FDroid.NoVersionException();
        if (apkUrl == null) throw new NoAppAssetException();

        DateTimeOffset? releaseDate = null;
        if (appDetails.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updated.GetString(), out var parsed))
            releaseDate = parsed;

        var apkName = apkUrl.Split('/').Last();
        return new AssetDetails(
            version,
            new List<KeyValuePair<string, string>> { new(apkName, apkUrl) },
            new AppNames(author, appName),
            releaseDate,
            null,
            new List<KeyValuePair<string, string>>(),
            null,
            null);
    }
}
