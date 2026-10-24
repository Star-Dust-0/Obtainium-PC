using System.Text.Json;
using ObtainiumPC.Sources.GitHub;

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

    public CodebergSource(CodebergSourceConfig? config = null)
    {
        _config = config ?? new CodebergSourceConfig();
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
            ApiHost: "https://codeberg.org/api/v1"));
        return gh.FetchReleaseDetailsWithTagFallback(standardUrl, cleaned, cancellationToken);
    }
}
