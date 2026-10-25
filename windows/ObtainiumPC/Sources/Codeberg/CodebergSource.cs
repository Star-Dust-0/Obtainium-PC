using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ObtainiumPC.Sources.GitHub;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Sources.Codeberg;

public sealed record CodebergSourceConfig(
    string? ForgejoPersonalAccessToken = null,
    int GlobalMinimumUpdateAgeDays = 0);

/// <summary>
/// Port of lib/app_sources/codeberg.dart: a Forgejo instance (codeberg.org)
/// reusing the GitHub release pipeline against the Forgejo API. The token is
/// 'forgejo-creds' (per-app override, then global), mapped onto the delegated
/// pipeline's 'github-creds' exactly like the original; GHReqPrefix and
/// checkRepoRename are GitHub-only and stripped.
/// </summary>
public sealed class CodebergSource : AppSourceBase
{
    private readonly CodebergSourceConfig _config;
    private readonly HttpClient? _http;

    /// <param name="http">Optional client (hermetic tests); null uses the shared app client.</param>
    public CodebergSource(CodebergSourceConfig? config = null, HttpClient? http = null)
    {
        _config = config ?? new CodebergSourceConfig();
        _http = http;
    }

    public override string Name => "Forgejo (Codeberg)";
    public override IReadOnlyList<string> Hosts => new[] { "codeberg.org" };
    public override bool ShowReleaseDateAsVersionToggle => true;
    public override bool AllowIncludeZips => true;
    public override bool AllowIncludeTarballs => true;

    // Same release options as the delegated GitHub pipeline.
    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Release options", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "includePrereleases", Label = "Include prereleases", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "fallbackToOlderReleases", Label = "Fallback to older releases", Kind = Forms.OptionFormItemKind.Switch, DefaultBool = true } },
            new Forms.OptionFormItem[] { new() { Key = "filterReleaseTitlesByRegEx", Label = "Filter release titles by regular expression", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = true } },
            new Forms.OptionFormItem[] { new() { Key = "invertReleaseTitlesFilter", Label = "Invert regular expression (Filter release titles by regular expression)", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "filterReleaseNotesByRegEx", Label = "Filter release notes by regular expression", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = true } },
            new Forms.OptionFormItem[] { new() { Key = "invertReleaseNotesFilter", Label = "Invert regular expression (Filter release notes by regular expression)", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "verifyLatestTag", Label = "Verify the 'latest' tag", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new()
            {
                Key = "sortMethodChoice", Label = "Sort method", Kind = Forms.OptionFormItemKind.ChoiceList, DefaultString = "date",
                Options = new KeyValuePair<string, string>[]
                {
                    new("date", "Release date"),
                    new("smartname", "Name (smart)"),
                    new("none", "None"),
                    new("smartname-datefallback", "Name (smart) x Release date"),
                    new("name", "Name"),
                }
            } },
            new Forms.OptionFormItem[] { new() { Key = "useLatestAssetDateAsReleaseDate", Label = "Use latest asset upload as release date", Kind = Forms.OptionFormItemKind.Switch } },
            new Forms.OptionFormItem[] { new() { Key = "releaseTitleAsVersion", Label = "Use release title as version string", Kind = Forms.OptionFormItemKind.Switch } },
        };

    // lib/app_sources/codeberg.dart search (searchCommon on the Forgejo API,
    // root prop "data").
    public override bool CanSearch => true;
    public override bool IncludeAdditionalOptsInMainSearch => true;

    public override async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> SearchAsync(
        string query, IReadOnlyDictionary<string, JsonElement> querySettings, CancellationToken ct = default)
    {
        // lib/app_sources/codeberg.dart search: host override via the dialog's
        // url field, Forgejo token from the dialog (codeberg.org tokens persist
        // in the source config), minStarCount filter via GitHub's searchCommon.
        var host = Hosts[0];
        var urlSetting = GetQueryString(querySettings, "url");
        if (urlSetting != null && Uri.TryCreate(urlSetting, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
            host = u.Host;
        using var http = ObtainiumHttp.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        var token = GetQueryString(querySettings, "forgejo-creds") ?? GetQueryString(querySettings, "github-creds");
        if (token != null)
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
        var res = await http.GetAsync(
            $"https://{host}/api/v1/repos/search?q=" + Uri.EscapeDataString(query) + "&limit=100", ct);
        res.EnsureSuccessStatusCode();
        var results = new Dictionary<string, IReadOnlyList<string>>();
        var data = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))?["data"]?.AsArray();
        if (data == null) return results;
        var minStarCount = GetQueryInt(querySettings, "minStarCount");
        foreach (var e in data)
        {
            var stars = e?["stars_count"]?.GetValue<int>() ?? e?["stargazers_count"]?.GetValue<int>() ?? 0;
            if (stars < minStarCount) continue;
            var htmlUrl = e?["html_url"]?.GetValue<string>();
            var fullName = e?["full_name"]?.GetValue<string>();
            if (htmlUrl == null || fullName == null) continue;
            var archived = e?["archived"]?.GetValue<bool>() == true;
            var desc = e?["description"]?.GetValue<string>() ?? "No description";
            results[htmlUrl] = new List<string> { fullName, (archived ? "[ARCHIVED] " : "") + desc };
        }
        return results;
    }

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false) =>
        StandardizeUrlWithRegex(url, subdomainPrefix: @"(www\.)?", pathPattern: @"/[^/]+/[^/]+");

    public override string? ChangeLogPageFromStandardUrl(string standardUrl) => standardUrl + "/releases";

    public override Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        // Map the Forgejo token onto the delegated pipeline's github-creds key.
        var cleaned = new Dictionary<string, JsonElement>(additionalSettings);
        var token = GetString(cleaned, "forgejo-creds");
        if (string.IsNullOrEmpty(token))
            token = GetString(cleaned, "github-creds"); // tokens saved by older versions
        if (string.IsNullOrEmpty(token))
            token = _config.ForgejoPersonalAccessToken;
        if (!string.IsNullOrEmpty(token))
            cleaned["github-creds"] = JsonSerializer.SerializeToElement(token);

        var gh = new GitHubSource(new GitHubSourceConfig(
            PersonalAccessToken: null,
            CheckRepoRename: false,
            UpdateHealthArchivedEnabled: false,
            GlobalMinimumUpdateAgeDays: _config.GlobalMinimumUpdateAgeDays,
            ApiHost: "https://codeberg.org/api/v1"), _http);
        return gh.FetchReleaseDetailsWithTagFallback(standardUrl, cleaned, cancellationToken);
    }
}
