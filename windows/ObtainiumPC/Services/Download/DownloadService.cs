using System.Diagnostics;
using System.Net.Http;

namespace ObtainiumPC.Services.Download;

/// <summary>
/// Downloads release assets and hands them to the user for installation.
/// Installer launching is always interactive (the OS shows its own prompt);
/// silent/unattended install modes are a separate, explicitly gated feature.
/// </summary>
public sealed class DownloadService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public static string DownloadDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ObtainiumPC");

    /// Downloads url into the ObtainiumPC downloads folder, reporting progress.
    /// Returns the full path of the downloaded file.
    public async Task<string> DownloadAsync(
        string url,
        string fileName,
        IProgress<(long Received, long? Total)>? progress = null,
        string? personalAccessToken = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(DownloadDir);
        var target = Path.Combine(DownloadDir, Sanitize(fileName));
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        // GitHub API asset URLs need the octet-stream accept header (and auth when set).
        if (!string.IsNullOrEmpty(personalAccessToken))
        {
            req.Headers.TryAddWithoutValidation("Authorization", "Token " + personalAccessToken);
            req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        }
        if (extraHeaders != null)
            foreach (var (key, value) in extraHeaders)
                req.Headers.TryAddWithoutValidation(key, value);
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength;
        await using var input = await res.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress?.Report((received, total));
        }
        return target;
    }

    private static string Sanitize(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName.Length > 0 ? fileName : "download.bin";
    }

    /// Opens the downloaded installer for the user. .msi goes through msiexec;
    /// everything else launches directly. Never silent, never auto-elevated.
    public static void OpenInstaller(string path)
    {
        if (path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\"") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// Reveals the file in Explorer.
    public static void ShowInFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
