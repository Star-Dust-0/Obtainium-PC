using System.Text.Json;
using ObtainiumPC.Sources.Html;

namespace ObtainiumPC.Sources.DirectLink;

/// <summary>
/// Port of lib/app_sources/direct_apk_link.dart (renamed for Windows: a direct
/// link to an installer/archive file). Delegates to the HTML source with
/// directAPKLink=true and version detection off; pseudo-versioning only.
/// </summary>
public sealed class DirectFileLinkSource : AppSourceBase
{
    private readonly HtmlSource _html = new();

    public override string Name => "Direct File Link";
    public override IReadOnlyList<string> Hosts => Array.Empty<string>();
    public override bool VersionDetectionDisallowed => true;
    public override IReadOnlyList<string> ExcludeCommonSettingKeys { get; } = new[]
    {
        "versionExtractionRegEx", "matchGroupToUse", "versionDetection",
        "useVersionCodeAsOSVersion", "apkFilterRegEx", "autoApkFilterByArch",
    };

    public override IReadOnlyList<IReadOnlyList<Forms.OptionFormItem>> AdditionalSourceAppSpecificSettingFormItems { get; } =
        new IReadOnlyList<Forms.OptionFormItem>[]
        {
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Request headers", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "requestHeader", Label = "Request headers (one per line, 'Name: value')", Kind = Forms.OptionFormItemKind.Text, SpecialCodec = "requestHeader" } },
            new Forms.OptionFormItem[] { new() { Key = "", Label = "Version options", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new()
            {
                Key = "defaultPseudoVersioningMethod", Label = "Pseudo-versioning method", Kind = Forms.OptionFormItemKind.ChoiceList, DefaultString = "partialAPKHash",
                Options = new KeyValuePair<string, string>[]
                {
                    new("partialAPKHash", "Partial file hash"),
                    new("ETag", "ETag"),
                }
            } },
            new Forms.OptionFormItem[] { new() { Key = "", Label = "ZIP options", Kind = Forms.OptionFormItemKind.Header } },
            new Forms.OptionFormItem[] { new() { Key = "zippedApkFilterRegEx", Label = "Filter assets inside ZIP", Kind = Forms.OptionFormItemKind.Text, ValidateRegex = true } },
        };

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        if (!forSelection)
            return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.ToString() : url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !AssetFilterService.IsAppOrContainerFile(uri.AbsolutePath, includeArchives: true, includeTarballs: true))
            throw new InvalidUrlException(Name);
        return url;
    }

    public override Task<AssetDetails> GetLatestAssetDetailsAsync(
        string standardUrl,
        IReadOnlyDictionary<string, JsonElement> additionalSettings,
        CancellationToken cancellationToken = default)
    {
        var settings = new Dictionary<string, JsonElement>(additionalSettings)
        {
            ["directAPKLink"] = JsonSerializer.SerializeToElement(true),
            ["versionDetection"] = JsonSerializer.SerializeToElement(false),
        };
        return _html.GetLatestAssetDetailsAsync(standardUrl, settings, cancellationToken);
    }
}
