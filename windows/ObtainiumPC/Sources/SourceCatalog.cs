using ObtainiumPC.Services.Settings;
using ObtainiumPC.Sources.GitHub;
using ObtainiumPC.Sources.Codeberg;
using ObtainiumPC.Sources.DirectLink;
using ObtainiumPC.Sources.FDroid;
using ObtainiumPC.Sources.GitLab;
using ObtainiumPC.Sources.ItchIo;
using ObtainiumPC.Sources.NeutronCode;
using ObtainiumPC.Sources.SourceHut;
using ObtainiumPC.Sources.SourceForge;

namespace ObtainiumPC.Sources;

/// <summary>
/// Resolves a URL to its app source. Shared by the update service and the UI so
/// source detection logic lives in exactly one place.
/// </summary>
public static class SourceCatalog
{
    /// All sources that can be picked as an explicit override (upstream's
    /// "Override Source" dropdown). HTML has no hosts of its own.
    public static IReadOnlyList<string> OverrideSourceNames => new[] { "GitHub", "GitLab", "Forgejo (Codeberg)", "SourceForge", "SourceHut", "NeutronCode", "itch.io", "F-Droid", "HTML", "Direct File Link" };

    public static AppSourceBase ResolveByName(string name, AppSettings settings) => name switch
    {
        "GitHub" => Resolve("https://github.com/x/y", settings),
        "GitLab" => Resolve("https://gitlab.com/x/y", settings),
        "Forgejo (Codeberg)" => Resolve("https://codeberg.org/x/y", settings),
        "SourceForge" => new SourceForge.SourceForgeSource(),
        "SourceHut" => new SourceHutSource(),
        "NeutronCode" => new NeutronCodeSource(),
        "itch.io" => new ItchIoSource(),
        "F-Droid" => new FDroidSource(),
        "HTML" => new Html.HtmlSource(),
        "Direct File Link" => new DirectFileLinkSource(),
        _ => throw new InvalidUrlException(name),
    };

    public static AppSourceBase Resolve(string url, AppSettings settings, string? overrideSource = null)
    {
        if (!string.IsNullOrEmpty(overrideSource)) return ResolveByName(overrideSource, settings);
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        if (host == "github.com" || host == "www.github.com")
            return new GitHubSource(new GitHubSourceConfig(
                PersonalAccessToken: settings.GitHubPersonalAccessToken,
                GhReqPrefix: settings.GitHubReqPrefix,
                CheckRepoRename: settings.GitHubCheckRepoRename,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "gitlab.com" || host == "www.gitlab.com")
            return new GitLabSource(new GitLabSourceConfig(
                PersonalAccessToken: settings.GitLabPersonalAccessToken,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "codeberg.org" || host == "www.codeberg.org")
            return new CodebergSource(new CodebergSourceConfig(
                ForgejoPersonalAccessToken: settings.ForgejoPersonalAccessToken,
                GlobalMinimumUpdateAgeDays: settings.MinimumUpdateAgeDays));
        if (host == "f-droid.org" || host == "www.f-droid.org")
            return new FDroidSource();
        if (host == "itch.io" || host.EndsWith(".itch.io"))
            return new ItchIoSource();
        if (host == "neutroncode.com" || host == "www.neutroncode.com")
            return new NeutronCodeSource();
        if (host == "git.sr.ht")
            return new SourceHutSource();
        if (host == "sourceforge.net" || host == "www.sourceforge.net")
            return new SourceForgeSource();
        throw new InvalidUrlException(host.Length > 0 ? host : url);
    }

    public static bool TryResolve(string url, AppSettings settings, out AppSourceBase? source, string? overrideSource = null)
    {
        try { source = Resolve(url, settings, overrideSource); return true; }
        catch (ObtainiumSourceException) { source = null; return false; }
    }
}
