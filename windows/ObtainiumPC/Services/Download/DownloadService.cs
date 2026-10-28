using System.Diagnostics;
using System.Net.Http;
using ObtainiumPC.Services.Http;

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
        var client = ObtainiumHttp.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Obtainium-PC");
        return client;
    }

    public static string DownloadDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ObtainiumPC");

    private readonly int _retries;
    private readonly TimeSpan _retryDelay;

    /// lib/providers/apps_provider.dart downloadFileWithRetry: 3 retries, 5 s apart (defaults).
    public DownloadService(int retries = 3, TimeSpan? retryDelay = null)
    {
        _retries = Math.Max(0, retries);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    /// Transient failures are retried: transport errors, timeouts, and HTTP 429 or 5xx. A user
    /// cancellation, other HTTP statuses, a wrong payload (web page, metadata, hash mismatch) are not.
    internal static bool IsRetryable(Exception e, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        return e switch
        {
            HttpRequestException h => h.StatusCode is null || (int)h.StatusCode == 429 || (int)h.StatusCode >= 500,
            TaskCanceledException => true, // HttpClient timeout (the caller did not cancel)
            IOException => true,
            _ => false,
        };
    }

    /// Downloads url into the ObtainiumPC downloads folder, reporting progress, retrying transient failures.
    /// Returns the full path of the downloaded file.
    public async Task<string> DownloadAsync(
        string url,
        string fileName,
        IProgress<(long Received, long? Total)>? progress = null,
        string? personalAccessToken = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        Func<string, Task>? verifyBeforePromote = null,
        CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(url, fileName, progress, personalAccessToken, extraHeaders, verifyBeforePromote, ct);
            }
            catch (Exception e) when (attempt < _retries && IsRetryable(e, ct))
            {
                await Task.Delay(_retryDelay, ct);
            }
        }
    }

    private async Task<string> DownloadOnceAsync(
        string url,
        string fileName,
        IProgress<(long Received, long? Total)>? progress = null,
        string? personalAccessToken = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        Func<string, Task>? verifyBeforePromote = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(DownloadDir);
        var target = Path.Combine(DownloadDir, Sanitize(fileName));
        var req = BuildRequest(url, personalAccessToken, extraHeaders);
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength;
        // Release-asset API metadata (JSON) is never an installer.
        if (mediaType0(res) is { } mt0 && mt0.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The server returned asset metadata instead of the file.");
        // A web page (proxy/ISP block page, login or error page) is never an installer.
        var mediaType = res.Content.Headers.ContentType?.MediaType;
        if (mediaType != null && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The server returned a web page instead of the file.");
        // Stream into <name>.part and move into place only after the checks pass: a
        // dropped connection, cancel or bad payload never leaves a partial file under the
        // installer's real name, and never destroys an earlier good download of it.
        var part = target + ".part";
        long received = 0;
        try
        {
            await using (var input = await res.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    progress?.Report((received, total));
                }
                await output.FlushAsync(ct);
            }
            VerifyDownloaded(part, fileName, received, total);
            // e.g. the release SHA-256 check: runs on the .part file, so a wrong-hash
            // download is discarded before it can replace an earlier good installer.
            if (verifyBeforePromote != null) await verifyBeforePromote(part);
            File.Move(part, target, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
        return target;
    }

    private static string? mediaType0(HttpResponseMessage res) => res.Content.Headers.ContentType?.MediaType;

    /// GitHub release-asset API URLs (api.github.com/.../releases/assets/ID) answer with
    /// JSON metadata unless the request asks for the binary, with or without a token.
    /// <summary>Best-effort size of a download without fetching the body: HEAD, then a headers-only GET.
    /// Null when unknown (never 0, never throws).</summary>
    public static async Task<long?> ProbeSizeAsync(string url, string? personalAccessToken = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null, CancellationToken ct = default)
    {
        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            try
            {
                using var req = BuildRequest(url, personalAccessToken, extraHeaders);
                req.Method = method;
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode) continue;
                var len = res.Content.Headers.ContentLength;
                if (len is > 0) return len;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
            catch { }
        }
        return null;
    }

    internal static HttpRequestMessage BuildRequest(string url, string? personalAccessToken, IReadOnlyDictionary<string, string>? extraHeaders)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(personalAccessToken))
            req.Headers.TryAddWithoutValidation("Authorization", "Token " + personalAccessToken);
        if (!string.IsNullOrEmpty(personalAccessToken) || IsGitHubAssetApiUrl(url))
            req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
        if (extraHeaders != null)
            foreach (var (key, value) in extraHeaders)
            {
                req.Headers.Remove(key);
                req.Headers.TryAddWithoutValidation(key, value);
            }
        return req;
    }

    internal static bool IsGitHubAssetApiUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal);

    /// When the release lists a SHA256SUMS-style asset, the download must match it;
    /// a mismatch deletes the file and throws. No checksum asset = nothing to check.
    public enum ChecksumStatus { Verified, NoChecksumPublished, ChecksumUnavailable, NotListed }

    public static Task<ChecksumStatus> VerifyAgainstReleaseChecksumAsync(
        string path, string fileName, IEnumerable<KeyValuePair<string, string>> releaseAssets, CancellationToken ct = default)
        => VerifyAgainstReleaseChecksumAsync(path, fileName, releaseAssets, FetchChecksumTextAsync, ct);

    /// Fetches checksum text through BuildRequest so GitHub asset-API URLs return the
    /// file itself (octet-stream) rather than JSON metadata.
    private static async Task<string?> FetchChecksumTextAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = BuildRequest(url, null, null);
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }

    public static async Task<ChecksumStatus> VerifyAgainstReleaseChecksumAsync(
        string path, string fileName, IEnumerable<KeyValuePair<string, string>> releaseAssets,
        Func<string, CancellationToken, Task<string?>> fetchText, CancellationToken ct = default)
    {
        var sums = releaseAssets.Where(a =>
            System.Text.RegularExpressions.Regex.IsMatch(a.Key, @"(sha-?256|checksums?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (sums.Count == 0) return ChecksumStatus.NoChecksumPublished;
        var anyFetched = false;
        // Prefer the checksum file named after this asset, then any other.
        var ordered = sums.OrderByDescending(a => a.Key.StartsWith(fileName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var sum in ordered)
        {
            var text = await fetchText(sum.Value, ct);
            if (text == null) continue; // unavailable: cannot verify with this file
            anyFetched = true;
            if (ExpectedSha256(text, fileName) == null) continue; // does not list this file
            if (ChecksumMismatch(path, fileName, text))
            {
                TryDelete(path);
                throw new InvalidDataException("The downloaded file does not match the published SHA-256 checksum.");
            }
            return ChecksumStatus.Verified; // listed and matching
        }
        return anyFetched ? ChecksumStatus.NotListed : ChecksumStatus.ChecksumUnavailable;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// Refuses to hand on a file that is empty, shorter than the server announced,
    /// or an .exe/.msi that is not a structurally valid Windows image. A truncated
    /// PE is what Windows reports as "Unsupported 16-Bit Application".
    public static void VerifyDownloaded(string path, string fileName, long received, long? expectedLength)
    {
        if (received == 0)
            throw new InvalidDataException("The download was empty.");
        if (expectedLength is > 0 && received != expectedLength)
            throw new InvalidDataException($"The download was incomplete ({received} of {expectedLength} bytes).");
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".exe"))
        {
            if (!IsValidPe(path))
                throw new InvalidDataException("The downloaded file is not a valid Windows executable.");
        }
        else if (lower.EndsWith(".msi"))
        {
            using var f = File.OpenRead(path);
            var sig = new byte[8];
            if (f.Read(sig, 0, 8) < 8 || sig[0] != 0xD0 || sig[1] != 0xCF || sig[2] != 0x11 || sig[3] != 0xE0)
                throw new InvalidDataException("The downloaded file is not a valid Windows Installer package.");
        }
    }

    public static bool IsValidPe(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            if (f.Length < 0x40) return false;
            var head = new byte[0x40];
            f.ReadExactly(head, 0, 0x40);
            if (head[0] != 'M' || head[1] != 'Z') return false;
            var peOffset = BitConverter.ToInt32(head, 0x3c);
            if (peOffset < 0x40 || peOffset + 24L > f.Length) return false;
            f.Position = peOffset;
            var pe = new byte[4];
            f.ReadExactly(pe, 0, 4);
            return pe[0] == 'P' && pe[1] == 'E' && pe[2] == 0 && pe[3] == 0;
        }
        catch { return false; }
    }

    /// Finds the expected SHA-256 for fileName in checksum-file text
    /// ("<hex>  name" / "<hex> *name" lines, or a bare hash file).
    public static string? ExpectedSha256(string checksumText, string fileName)
    {
        var lines = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && Path.GetFileName(m.Groups[2].Value.Trim()).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        if (lines.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"^[0-9a-fA-F]{64}$"))
            return lines[0].ToLowerInvariant();
        return null;
    }

    public static string Sha256Of(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)).ToLowerInvariant();
    }

    /// True when a checksum file lists this asset and the hash differs.
    public static bool ChecksumMismatch(string path, string fileName, string checksumText)
    {
        var expected = ExpectedSha256(checksumText, fileName);
        return expected != null && !expected.Equals(Sha256Of(path), StringComparison.Ordinal);
    }

    private static string Sanitize(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName.Length > 0 ? fileName : "download.bin";
    }

    /// Opens the downloaded installer for the user. .msi goes through msiexec;
    /// everything else launches directly. Never silent, never auto-elevated.
    public static Process? OpenInstaller(string path)
    {
        if (path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            return Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\"") { UseShellExecute = true });
        return Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// Reveals the file in Explorer.
    public static void ShowInFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
