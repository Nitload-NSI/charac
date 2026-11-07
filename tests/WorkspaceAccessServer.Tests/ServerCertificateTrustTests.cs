using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Charac.Client;

namespace Charac.Server.Tests;

public sealed class ServerCertificateTrustTests
{
    [Fact]
    public void ReusedPublicKeyKeepsFingerprintAcrossCertificateRenewal()
    {
        using var key = RSA.Create(2048);
        using var first = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var renewed = CreateCertificate(key, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));

        Assert.Equal(ServerCertificateTrust.GetSpkiFingerprint(first),
            ServerCertificateTrust.GetSpkiFingerprint(renewed));
    }

    [Fact]
    public void UntrustedValidCertificateRequiresFirstUseConfirmation()
    {
        using var certificate = CreateCertificate(RSA.Create(2048), DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var origin = new Uri("https://192.0.2.10:65534/");
        var fingerprint = ServerCertificateTrust.GetSpkiFingerprint(certificate);

        var firstUse = ServerCertificateTrust.Evaluate(origin, origin, certificate,
            [X509ChainStatusFlags.UntrustedRoot], SslPolicyErrors.RemoteCertificateChainErrors,
            trustedFingerprint: null, DateTimeOffset.UtcNow);
        var pinned = ServerCertificateTrust.Evaluate(origin, origin, certificate,
            [X509ChainStatusFlags.UntrustedRoot], SslPolicyErrors.RemoteCertificateChainErrors,
            fingerprint, DateTimeOffset.UtcNow);
        var mismatchedHost = ServerCertificateTrust.Evaluate(origin, origin, certificate,
            [X509ChainStatusFlags.UntrustedRoot],
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
            fingerprint, DateTimeOffset.UtcNow);

        Assert.Equal(CertificateTrustKind.NeedsConfirmation, firstUse.Kind);
        Assert.Equal(CertificateTrustKind.Pinned, pinned.Kind);
        Assert.Equal(CertificateTrustKind.Rejected, mismatchedHost.Kind);
    }

    [Fact]
    public void SystemTrustedCertificatesNeedNoTofuPrompt()
    {
        using var certificate = CreateCertificate(RSA.Create(2048), DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var origin = new Uri("https://access.example.com/");

        var decision = ServerCertificateTrust.Evaluate(origin, origin, certificate, [],
            SslPolicyErrors.None, trustedFingerprint: null, DateTimeOffset.UtcNow);

        Assert.Equal(CertificateTrustKind.SystemTrusted, decision.Kind);
    }

    [Fact]
    public void ChangedKeyRequiresExplicitReplacementConfirmation()
    {
        using var oldKey = RSA.Create(2048);
        using var newKey = RSA.Create(2048);
        using var oldCertificate = CreateCertificate(oldKey, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        using var newCertificate = CreateCertificate(newKey, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var origin = new Uri("https://192.0.2.10:65534/");

        var decision = ServerCertificateTrust.Evaluate(origin, origin, newCertificate,
            [X509ChainStatusFlags.UntrustedRoot], SslPolicyErrors.RemoteCertificateChainErrors,
            ServerCertificateTrust.GetSpkiFingerprint(oldCertificate), DateTimeOffset.UtcNow);

        Assert.Equal(CertificateTrustKind.NeedsConfirmation, decision.Kind);
        Assert.True(decision.ReplacingExistingPin);
    }

    [Fact]
    public void TrustPinsAreScopedToExactOriginIncludingPort()
    {
        var directory = Path.Combine(Path.GetTempPath(), "charac-server-trust-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "server-trust.json");
        var origin = new Uri("https://192.0.2.10:65534/");
        const string fingerprint = "SHA256:trusted-key";
        try
        {
            ServerCertificateTrust.SavePin(path, origin, fingerprint);

            Assert.Equal(fingerprint, ServerCertificateTrust.LoadPin(path, origin)?.SpkiSha256);
            Assert.Null(ServerCertificateTrust.LoadPin(path, new Uri("https://192.0.2.10:9443/")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static X509Certificate2 CreateCertificate(RSA key, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var request = new CertificateRequest("CN=192.0.2.10", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(System.Net.IPAddress.Parse("192.0.2.10"));
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
