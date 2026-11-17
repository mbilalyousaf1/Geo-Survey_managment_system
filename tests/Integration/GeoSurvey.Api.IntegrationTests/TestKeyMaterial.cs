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
        Settings = new()
        {
            ["DataProtection:ApplicationName"] = "GeoSurvey.Tests",
            ["DataProtection:KeyRingPath"] = ring,
            ["DataProtection:CertificatePath"] = path,
            ["DataProtection:CertificatePassword"] = password
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
