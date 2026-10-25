using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ObtainiumPC.Services.Http;

/// <summary>
/// HTTP client factory carrying upstream's TLS behavior
/// (lib/services/http_service.dart): when enableCertificatePinning is on,
/// connections to the pinned hosts below (and their subdomains, via the
/// two-label root host) trust ONLY the CA roots bundled from upstream
/// assets/ca-certs. The RuStore Mintsifry-root workaround applies whether or
/// not pinning is on, matching upstream.
/// </summary>
public static class ObtainiumHttp
{
    // lib/services/http_service.dart _certificatePins.
    private static readonly Dictionary<string, string[]> PinFiles = new()
    {
        ["github.com"] = new[] { "sectigo-pub-serv-auth-r46.crt", "sectigo-pub-serv-auth-e46.crt", "isrg-root-x1.crt", "isrg-root-x2.crt", "isrg-root-ye.crt", "isrg-root-yr.crt" },
        ["codeberg.org"] = new[] { "isrg-root-x1.crt", "isrg-root-x2.crt", "isrg-root-ye.crt", "isrg-root-yr.crt" },
        ["gitlab.com"] = new[] { "sectigo-pub-serv-auth-r46.crt", "sectigo-pub-serv-auth-e46.crt" },
        ["rustore.ru"] = new[] { "harica-tls-root-2021-rsa.crt", "harica-tls-root-2021-ecc.crt", "russian-mintsifry-root.crt" },
    };

    // Tests override the cert directory with fabricated fixtures.
    internal static string? CertDirOverride;
    private static string CertDir => CertDirOverride ?? Path.Combine(AppContext.BaseDirectory, "ca-certs");

    /// Whether pinning is active. The app wires this to App.Settings at
    /// startup; kept injectable so the pure-logic test project (which links
    /// sources but not the WinUI App class) compiles and defaults to off.
    public static Func<bool> PinningEnabled = () => false;

    /// lib/services/http_service.dart extractRootHost (last two labels).
    public static string ExtractRootHost(string host)
    {
        var parts = host.Split('.');
        return parts.Length > 2 ? string.Join('.', parts[^2..]) : host;
    }

    public static HttpClient CreateClient(bool allowAutoRedirect = true)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect };
        handler.ServerCertificateCustomValidationCallback = ValidateCertificate;
        return new HttpClient(handler);
    }

    // Internal for unit tests; the handler callback is the production entry.
    internal static bool ValidateCertificate(
        HttpRequestMessage request, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (cert == null) return false;
        var host = request.RequestUri?.Host ?? "";
        var root = ExtractRootHost(host);
        // Dart's HttpClient always verifies hostname and validity on top of a
        // custom SecurityContext; a custom trust store here must never waive
        // them. The default evaluation's name-mismatch bit is authoritative
        // for the hostname check on every path below.
        var nameMismatch = (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0;
        if (nameMismatch) return false;
        if (PinningEnabled())
        {
            var pinned = PinFiles.ContainsKey(host) ? host : PinFiles.ContainsKey(root) ? root : null;
            if (pinned != null)
                // Trust ONLY the bundled roots for this host. X509Chain still
                // enforces expiry/signatures; hostname was checked above.
                return ValidateWithRoots(cert, PinFiles[pinned]);
        }
        if (errors == SslPolicyErrors.None) return true;
        // lib/services/http_service.dart _ruStoreWorkaroundSecurityContext:
        // rustore.ru may serve the Russian Mintsifry CA chain, which Windows
        // does not trust; accept it when the chain builds against the bundled
        // Mintsifry root (system-valid chains already returned above).
        if (root == "rustore.ru")
            return ValidateWithRoots(cert, new[] { "russian-mintsifry-root.crt" });
        return false;
    }

    private static bool ValidateWithRoots(X509Certificate2 cert, string[] files)
    {
        try
        {
            using var custom = new X509Chain();
            custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            custom.ChainPolicy.RevocationMode = X509ChainRevocationMode.NoCheck;
            // serverAuth EKU, as the platform TLS verifier requires.
            custom.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            var roots = 0;
            foreach (var f in files)
            {
                var p = Path.Combine(CertDir, f);
                if (File.Exists(p)) { custom.ChainPolicy.CustomTrustStore.Add(new X509Certificate2(p)); roots++; }
            }
            // Pinning fails closed: no bundled roots means no trust.
            if (roots == 0) return false;
            return custom.Build(cert);
        }
        catch { return false; }
    }
}
