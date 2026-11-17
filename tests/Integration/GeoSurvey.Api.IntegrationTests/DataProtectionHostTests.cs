using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using GeoSurvey.Api.Configuration;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeoSurvey.Api.IntegrationTests;

public sealed class DataProtectionHostTests
{
    private static IHost Host(Dictionary<string, string?> settings) => new HostBuilder()
        .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings))
        .ConfigureServices((context, services) => services.AddGeoSurveyDataProtection(context.Configuration, context.HostingEnvironment))
        .Build();

    [Fact]
    public async Task Restart_PreservesSecret_AndKeyFilesAreEncrypted()
    {
        using var material = new TestKeyMaterial();
        var account = Guid.NewGuid();
        string ciphertext;
        using (var first = Host(material.Settings))
        {
            await first.StartAsync();
            ciphertext = first.Services.GetRequiredService<ITotpSecretProtector>().Protect(account, "synthetic-secret");
            await first.StopAsync();
        }
        var files = Directory.GetFiles(material.Settings["DataProtection:KeyRingPath"]!, "key-*.xml");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var xml = XDocument.Load(file);
            Assert.Contains(xml.Descendants(), element => element.Name.LocalName == "EncryptedData");
            Assert.DoesNotContain(xml.Descendants(), element => element.Name.LocalName == "masterKey");
        }
        using var second = Host(material.Settings);
        await second.StartAsync();
        Assert.Equal("synthetic-secret", second.Services.GetRequiredService<ITotpSecretProtector>().Unprotect(account, ciphertext));
        await second.StopAsync();
        var isolated = new Dictionary<string, string?>(material.Settings) { ["DataProtection:ApplicationName"] = "GeoSurvey.OtherEnvironment" };
        using var other = Host(isolated);
        await other.StartAsync();
        Assert.Throws<CryptographicException>(() => other.Services.GetRequiredService<ITotpSecretProtector>().Unprotect(account, ciphertext));
        await other.StopAsync();
    }

    [Theory]
    [InlineData("ApplicationName", null)]
    [InlineData("KeyRingPath", "relative-do-not-echo")]
    [InlineData("CertificatePath", "relative-do-not-echo")]
    [InlineData("CertificatePassword", null)]
    [InlineData("CertificatePassword", "incorrect-do-not-echo")]
    public async Task InvalidConfiguration_BlocksStartupWithoutEcho(string setting, string? value)
    {
        using var material = new TestKeyMaterial();
        var settings = new Dictionary<string, string?>(material.Settings) { ["DataProtection:" + setting] = value };
        using var host = Host(settings);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        Assert.DoesNotContain("do-not-echo", error.ToString());
        Assert.DoesNotContain(material.Settings["DataProtection:CertificatePassword"]!, error.ToString());
    }

    [Fact]
    public async Task CertificateWithoutPrivateKey_BlocksStartup()
    {
        using var material = new TestKeyMaterial();
        var path = material.Settings["DataProtection:CertificatePath"]!;
        var password = material.Settings["DataProtection:CertificatePassword"]!;
        using var original = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        using var publicOnly = X509CertificateLoader.LoadCertificate(original.Export(X509ContentType.Cert));
        File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pfx, password));
        using var host = Host(material.Settings);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(password, error.ToString());
    }

    [Theory]
    [InlineData("<broken-do-not-echo")]
    [InlineData("<key><descriptor><masterKey>do-not-echo</masterKey></descriptor></key>")]
    public async Task MalformedOrUnencryptedKeyFile_BlocksStartup(string xml)
    {
        using var material = new TestKeyMaterial();
        File.WriteAllText(Path.Combine(material.Settings["DataProtection:KeyRingPath"]!, "key-test.xml"), xml);
        using var host = Host(material.Settings);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("do-not-echo", error.ToString());
    }

    [Fact]
    public async Task RepositoryKeyDirectory_IsRejected()
    {
        using var material = new TestKeyMaterial();
        var settings = new Dictionary<string, string?>(material.Settings)
        {
            ["DataProtection:KeyRingPath"] = Directory.GetCurrentDirectory()
        };
        using var host = Host(settings);
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task WrongCertificateForExistingRing_BlocksStartup()
    {
        using var firstMaterial = new TestKeyMaterial();
        using var wrongMaterial = new TestKeyMaterial();
        using (var first = Host(firstMaterial.Settings))
        {
            await first.StartAsync();
            await first.StopAsync();
        }
        var settings = new Dictionary<string, string?>(wrongMaterial.Settings)
        {
            ["DataProtection:KeyRingPath"] = firstMaterial.Settings["DataProtection:KeyRingPath"]
        };
        using var wrong = Host(settings);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => wrong.StartAsync());
        Assert.Null(error.InnerException);
    }
}
