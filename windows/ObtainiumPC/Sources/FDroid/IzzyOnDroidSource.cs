namespace ObtainiumPC.Sources.FDroid;

/// <summary>
/// Port of lib/app_sources/izzyondroid.dart: IzzyOnDroid's F-Droid-compatible
/// repo. Reuses the F-Droid package-API pipeline with Izzy's hosts.
/// Track-only on Windows.
/// </summary>
public sealed class IzzyOnDroidSource : FDroidSource
{
    public override string Name => "IzzyOnDroid";
    public override IReadOnlyList<string> Hosts => new[] { "izzysoft.de" };
    public override bool IsOfficialHost => false;

    public override string GetApiUrl(string appId) =>
        $"https://apt.izzysoft.de/fdroid/api/v1/packages/{appId}";

    public override string GetApkUrlPrefix(string appId) =>
        $"https://android.izzysoft.de/frepo/{appId}";

    public override string SourceSpecificStandardizeUrl(string url, bool forSelection = false)
    {
        var host = new Uri(url).Host;
        if (host.StartsWith("android."))
            return StandardizeUrlWithRegex(url, subdomainPrefix: @"android\.", pathPattern: @"/repo/apk/[^/]+");
        return StandardizeUrlWithRegex(url, subdomainPrefix: @"apt\.", pathPattern: @"/fdroid/index/apk/[^/]+");
    }
}
