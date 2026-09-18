using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Sonarr.MetadataProxy.Options;

namespace Sonarr.MetadataProxy.Tls;

public sealed class CertificateProvider
{
    public const string HostName = "skyhook.sonarr.tv";

    private readonly ProxyOptions _options;
    private readonly ILogger<CertificateProvider> _logger;
    private X509Certificate2? _leaf;

    public CertificateProvider(ProxyOptions options, ILogger<CertificateProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public X509Certificate2 GetOrCreateServerCertificate()
    {
        if (_leaf is not null)
        {
            return _leaf;
        }

        var certDirectory = Path.Combine(_options.DataDir, "certs");
        Directory.CreateDirectory(certDirectory);

        var caCertPath = Path.Combine(certDirectory, "ca.crt");
        var caKeyPath = Path.Combine(certDirectory, "ca.key");
        var serverCertPath = Path.Combine(certDirectory, "server.crt");
        var serverKeyPath = Path.Combine(certDirectory, "server.key");

        if (File.Exists(caCertPath) && File.Exists(caKeyPath) &&
            File.Exists(serverCertPath) && File.Exists(serverKeyPath))
        {
            _logger.LogInformation("Loading existing proxy certificates from {Directory}.", certDirectory);
            _leaf = X509Certificate2.CreateFromPemFile(serverCertPath, serverKeyPath);
            return _leaf;
        }

        _logger.LogInformation(
            "No proxy certificates found in {Directory}. Generating a new development CA and a server certificate for {Host}.",
            certDirectory,
            HostName);

        var now = DateTimeOffset.UtcNow;
        var notBefore = now.AddDays(-1);
        var caNotAfter = now.AddYears(10);
        var leafNotAfter = now.AddDays(397);

        if (File.Exists(caCertPath)) File.Delete(caCertPath);
        if (File.Exists(caKeyPath)) File.Delete(caKeyPath);
        if (File.Exists(serverCertPath)) File.Delete(serverCertPath);
        if (File.Exists(serverKeyPath)) File.Delete(serverKeyPath);

        using var caRsa = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            new X500DistinguishedName("CN=Sonarr Metadata Proxy, O=Sonarr Metadata Proxy"),
            caRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, false));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));

        using var caCertificate = caRequest.CreateSelfSigned(notBefore, caNotAfter);

        using var leafRsa = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            new X500DistinguishedName($"CN={HostName}, O=Sonarr Metadata Proxy"),
            leafRsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName(HostName);
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        leafRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;

        var leafPrepared = leafRequest.Create(caCertificate, notBefore, leafNotAfter, serial);
        var leafWithKey = leafPrepared.CopyWithPrivateKey(leafRsa);

        File.WriteAllText(caCertPath, caCertificate.ExportCertificatePem());
        WritePrivateKeyPem(caKeyPath, caCertificate.GetRSAPrivateKey()!);
        File.WriteAllText(serverCertPath, leafWithKey.ExportCertificatePem());
        WritePrivateKeyPem(serverKeyPath, leafWithKey.GetRSAPrivateKey()!);

        _logger.LogInformation(
            "Wrote CA and server certificate to {Directory}. Trust {Ca} inside the Sonarr container to enable TLS interception.",
            certDirectory,
            caCertPath);

        _leaf = X509Certificate2.CreateFromPemFile(serverCertPath, serverKeyPath);
        return _leaf;
    }

    private static void WritePrivateKeyPem(string path, RSA key)
    {
        var keyBytes = key.ExportPkcs8PrivateKey();
        var base64 = Convert.ToBase64String(keyBytes, Base64FormattingOptions.InsertLineBreaks);
        File.WriteAllText(path,
            "-----BEGIN PRIVATE KEY-----\n" + base64 + "\n-----END PRIVATE KEY-----\n");
    }
}