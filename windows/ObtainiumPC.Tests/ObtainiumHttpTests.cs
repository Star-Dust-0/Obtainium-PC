using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ObtainiumPC.Services.Http;
using Xunit;

namespace ObtainiumPC.Tests;

/// <summary>
/// ObtainiumHttp certificate validation, grounded in upstream
/// lib/services/http_service.dart: pinning restricts pinned hosts to the
/// bundled CA roots, hostname/expiry are never waived, non-pinned hosts keep
/// default platform validation, and the RuStore Mintsifry workaround applies
/// without pinning.
/// </summary>
public class ObtainiumHttpTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "obt-certs-" + Guid.NewGuid().ToString("N"));

    public ObtainiumHttpTests()
    {
        Directory.CreateDirectory(_dir);
        ObtainiumHttp.CertDirOverride = _dir;
        ObtainiumHttp.PinningEnabled = () => false;
    }

    public void Dispose()
    {
        ObtainiumHttp.CertDirOverride = null;
        ObtainiumHttp.PinningEnabled = () => false;
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---- fixtures ---------------------------------------------------------

    private sealed record Chain(X509Certificate2 Ca, X509Certificate2 Leaf);

    private static Chain MakeChain(string caName, string leafName, bool expired = false)
    {
        var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=" + caName, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caReq.PublicKey, false));
        var ca = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(5));

        var leafKey = RSA.Create(2048);
        var leafReq = new CertificateRequest("CN=" + leafName, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(leafName);
        leafReq.CertificateExtensions.Add(san.Build());
        leafReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth
        var leaf = leafReq.CreateSigned(ca,
            expired ? DateTimeOffset.UtcNow.AddYears(-2) : DateTimeOffset.UtcNow.AddDays(-2),
            expired ? DateTimeOffset.UtcNow.AddYears(-1) : DateTimeOffset.UtcNow.AddYears(1),
            new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 });
        caKey.Dispose();
        leafKey.Dispose();
        return new Chain(ca, leaf);
    }

    private void PinAs(X509Certificate2 ca, params string[] files)
    {
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_dir, f), ca.ExportCertificatePem());
    }

    private static HttpRequestMessage Req(string url) => new(HttpMethod.Get, url);

    private const string GitHubPinFile = "sectigo-pub-serv-auth-r46.crt";
    private const string MintsifryFile = "russian-mintsifry-root.crt";

    // ---- pin matching / subdomain boundaries (upstream extractRootHost) ---

    [Theory]
    [InlineData("api.github.com", "github.com")]
    [InlineData("github.com", "github.com")]
    [InlineData("release-assets.githubusercontent.com", "githubusercontent.com")]
    [InlineData("localhost", "localhost")]
    [InlineData("a.b.co.uk", "co.uk")] // naive two-label root, same as upstream
    public void ExtractRootHost_matches_upstream(string host, string root)
        => Assert.Equal(root, ObtainiumHttp.ExtractRootHost(host));

    // ---- pinning on --------------------------------------------------------

    [Fact]
    public void Pinned_host_valid_chain_against_bundled_root_is_accepted()
    {
        var c = MakeChain("Test GH CA", "api.github.com");
        PinAs(c.Ca, GitHubPinFile);
        ObtainiumHttp.PinningEnabled = () => true;
        // The default (system) evaluation did not trust this CA - pinning is
        // what rescues it, like a private-CA github.com deployment.
        Assert.True(ObtainiumHttp.ValidateCertificate(
            Req("https://api.github.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Pinned_host_signed_by_an_unrelated_trusted_ca_is_rejected()
    {
        var good = MakeChain("Real GH CA", "api.github.com");
        var evil = MakeChain("Some Other Trusted CA", "api.github.com");
        // Only the unrelated CA is pinned - a valid chain to it must fail
        // because it is not the upstream-bundled root.
        PinAs(evil.Ca, GitHubPinFile);
        ObtainiumHttp.PinningEnabled = () => true;
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://api.github.com/x"), good.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Pinned_host_expired_certificate_is_rejected()
    {
        var c = MakeChain("Test GH CA", "api.github.com", expired: true);
        PinAs(c.Ca, GitHubPinFile);
        ObtainiumHttp.PinningEnabled = () => true;
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://api.github.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Pinned_host_name_mismatch_is_rejected_even_with_a_valid_pinned_chain()
    {
        var c = MakeChain("Test GH CA", "api.github.com");
        PinAs(c.Ca, GitHubPinFile);
        ObtainiumHttp.PinningEnabled = () => true;
        // A MITM presenting a pinned-CA cert for a DIFFERENT name: the
        // custom root must never waive the hostname check (Dart keeps it).
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://api.github.com/x"), c.Leaf, null,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void Pinned_host_missing_bundled_roots_fails_closed()
    {
        var c = MakeChain("Test GH CA", "api.github.com"); // nothing written to the dir
        ObtainiumHttp.PinningEnabled = () => true;
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://api.github.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Pinning_applies_through_the_two_label_root_host_boundary()
    {
        // api.github.com is pinned via the github.com root entry; a host
        // whose root differs (githubusercontent.com) is NOT pinned.
        var c = MakeChain("Test GH CA", "release-assets.githubusercontent.com");
        PinAs(c.Ca, GitHubPinFile);
        ObtainiumHttp.PinningEnabled = () => true;
        // Not a pinned host: default evaluation rules apply, so a chain the
        // platform rejected stays rejected.
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://release-assets.githubusercontent.com/x"), c.Leaf, null,
            SslPolicyErrors.RemoteCertificateChainErrors));
        // ...and a platform-valid chain passes untouched.
        Assert.True(ObtainiumHttp.ValidateCertificate(
            Req("https://release-assets.githubusercontent.com/x"), c.Leaf, null, SslPolicyErrors.None));
    }

    [Fact]
    public void Non_pinned_host_keeps_default_validation_when_pinning_on()
    {
        var c = MakeChain("Any CA", "downloads.example.com");
        ObtainiumHttp.PinningEnabled = () => true;
        Assert.True(ObtainiumHttp.ValidateCertificate(Req("https://downloads.example.com/x"), c.Leaf, null, SslPolicyErrors.None));
        Assert.False(ObtainiumHttp.ValidateCertificate(Req("https://downloads.example.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    // ---- pinning off -------------------------------------------------------

    [Fact]
    public void Pinning_off_leaves_default_validation_untouched()
    {
        var c = MakeChain("Test GH CA", "api.github.com");
        PinAs(c.Ca, GitHubPinFile); // pins exist but the toggle is off
        Assert.True(ObtainiumHttp.ValidateCertificate(Req("https://api.github.com/x"), c.Leaf, null, SslPolicyErrors.None));
        Assert.False(ObtainiumHttp.ValidateCertificate(Req("https://api.github.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    // ---- RuStore workaround (always on) ------------------------------------

    [Fact]
    public void Rustore_mintsifry_chain_is_accepted_with_or_without_pinning()
    {
        var c = MakeChain("Russian Mintsifry", "update.rustore.ru");
        PinAs(c.Ca, MintsifryFile);
        Assert.True(ObtainiumHttp.ValidateCertificate(
            Req("https://update.rustore.ru/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void Rustore_workaround_never_waives_hostname()
    {
        var c = MakeChain("Russian Mintsifry", "update.rustore.ru");
        PinAs(c.Ca, MintsifryFile);
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://update.rustore.ru/x"), c.Leaf, null,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void Non_rustore_broken_chain_is_never_rescued()
    {
        var c = MakeChain("Russian Mintsifry", "downloads.example.com");
        PinAs(c.Ca, MintsifryFile);
        Assert.False(ObtainiumHttp.ValidateCertificate(
            Req("https://downloads.example.com/x"), c.Leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }
}
