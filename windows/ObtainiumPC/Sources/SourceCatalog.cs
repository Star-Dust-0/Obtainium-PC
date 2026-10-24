using ObtainiumPC.Services.Settings;
using ObtainiumPC.Sources.GitHub;
using ObtainiumPC.Sources.Codeberg;
using ObtainiumPC.Sources.GitLab;

namespace ObtainiumPC.Sources;

/// <summary>
/// Resolves a URL to its app source. Shared by the update service and the UI so
/// source detection logic lives in exactly one place.
/// </summary>
public static class SourceCatalog
{
    public static AppSourceBase Resolve(string url, AppSettings settings)
    {
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
        throw new InvalidUrlException(host.Length > 0 ? host : url);
    }

    public static bool TryResolve(string url, AppSettings settings, out AppSourceBase? source)
    {
        try { source = Resolve(url, settings); return true; }
        catch (ObtainiumSourceException) { source = null; return false; }
    }
}
