using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ObtainiumPC.Services.Settings;
using ObtainiumPC.Sources;
using ObtainiumPC.Services.Http;

namespace ObtainiumPC.Services.BulkImport;

/// <summary>
/// Port of lib/app_sources/githubstars.dart: bulk-import the starred repos of
/// a GitHub user, paged at 100 per request with per-page rate-limit checks.
/// </summary>
public static class GitHubStarsService
{
    public sealed record StarredRepo(string Url, string FullName, string Description);

    public static async Task<List<StarredRepo>> GetStarredReposAsync(
        string username, AppSettings settings, CancellationToken ct = default)
    {
        using var http = ObtainiumHttp.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        if (!string.IsNullOrEmpty(settings.GitHubPersonalAccessToken))
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.GitHubPersonalAccessToken);

        var results = new List<StarredRepo>();
        var page = 1;
        while (true)
        {
            var url = $"https://api.github.com/users/{Uri.EscapeDataString(username)}/starred?per_page=100&page={page}";
            var res = await http.GetAsync(url, ct);
            if ((int)res.StatusCode == 403 &&
                res.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                remaining.FirstOrDefault() == "0")
                throw new ObtainiumHttpException(403, "GitHub rate limit reached");
            if ((int)res.StatusCode >= 400)
                throw new ObtainiumHttpException((int)res.StatusCode, url);

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var count = 0;
            foreach (var repo in doc.RootElement.EnumerateArray())
            {
                var htmlUrl = repo.GetProperty("html_url").GetString();
                if (htmlUrl == null) continue;
                var fullName = repo.GetProperty("full_name").GetString() ?? htmlUrl;
                var description = repo.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString() ?? "" : "";
                results.Add(new StarredRepo(htmlUrl, fullName, description));
                count++;
            }
            if (count < 100) break;
            page++;
        }
        return results;
    }
}
