using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GeoSurvey.Api.IntegrationTests;

internal sealed class TestKeyMaterial : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GeoSurvey-KeyTests-" + Guid.NewGuid().ToString("N"));
    public Dictionary<string, string?> Settings { get; }

    public TestKeyMaterial()
    {
        Directory.CreateDirectory(_root);
        var ring = Directory.CreateDirectory(Path.Combine(_root, "ring")).FullName;
        var path = Path.Combine(_root, "test.pfx");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=GeoSurvey synthetic test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        var signingPath = Path.Combine(_root, "signing.pfx");
        var signingPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var signingRsa = RSA.Create(2048);
        var signingRequest = new CertificateRequest("CN=GeoSurvey synthetic signing", signingRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var signingCertificate = signingRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        File.WriteAllBytes(signingPath, signingCertificate.Export(X509ContentType.Pfx, signingPassword));
        Settings = new()
        {
            ["DataProtection:ApplicationName"] = "GeoSurvey.Tests",
            ["DataProtection:KeyRingPath"] = ring,
            ["DataProtection:CertificatePath"] = path,
            ["DataProtection:CertificatePassword"] = password,
            ["Authentication:Issuer"] = "https://geosurvey.example.test",
            ["Authentication:Audience"] = "geosurvey-tests",
            ["Authentication:SigningCertificatePath"] = signingPath,
            ["Authentication:SigningCertificatePassword"] = signingPassword,
            ["Tenancy:LocalDemo:RegionKey"] = "local-demo",
            ["Tenancy:LocalDemo:RegionDisplayName"] = "Local test region",
            ["Tenancy:LocalDemo:RuntimeConnectionString"] = ConfiguredApiFactory.OfflineConnection,
            ["Tenancy:LocalDemo:RuntimeLogin"] = "GeoSurveyRuntime"
        };
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(_root);
        var ownedPrefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GeoSurvey-KeyTests-"));
        if (!resolved.StartsWith(ownedPrefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a test key directory outside its owned prefix.");
        if (Directory.Exists(resolved))
            Directory.Delete(resolved, recursive: true);
    }
}
