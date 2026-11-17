using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Api.Configuration;

public static class DataProtectionRegistration
{
    public static IServiceCollection AddGeoSurveyDataProtection(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<HostKeyOptions>().Bind(configuration.GetSection("DataProtection"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApplicationName), "DataProtection:ApplicationName is required and must be stable and environment-specific.")
            .Validate(options => ExternalPath(options.KeyRingPath, environment.ContentRootPath) && Directory.Exists(options.KeyRingPath),
                "DataProtection:KeyRingPath must be an existing absolute directory outside the application and repository.")
            .Validate(options => ExternalPath(options.CertificatePath, environment.ContentRootPath) && File.Exists(options.CertificatePath),
                "DataProtection:CertificatePath must be an existing absolute PFX path outside the application and repository.")
            .Validate(options => !string.IsNullOrEmpty(options.CertificatePassword), "DataProtection:CertificatePassword must be supplied through secret configuration.")
            .ValidateOnStart();
        services.AddSingleton<HostCertificate>();
        services.AddSingleton<HostProtectionProvider>();
        services.AddSingleton<IDataProtectionProvider>(provider => provider.GetRequiredService<HostProtectionProvider>());
        services.AddSingleton<ITotpSecretProtector, DataProtectionTotpSecretProtector>();
        services.AddHostedService<KeyRingStartupCheck>();
        return services;
    }

    private static bool ExternalPath(string? path, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        try
        {
            var full = Path.GetFullPath(path);
            for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
            {
                if (directory.FullName == Path.GetFullPath(contentRoot) || Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    var relative = Path.GetRelativePath(directory.FullName, full);
                    if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                        return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed class HostKeyOptions
{
    public string? ApplicationName { get; set; }
    public string? KeyRingPath { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}

internal sealed class HostCertificate : IDisposable
{
    public X509Certificate2 Value { get; }

    public HostCertificate(IOptions<HostKeyOptions> settings)
    {
        var options = settings.Value;
        X509Certificate2? loaded = null;
        try
        {
            loaded = X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, options.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
            using var key = loaded.GetRSAPrivateKey();
            var now = DateTime.UtcNow;
            if (key is null || loaded.NotBefore.ToUniversalTime() > now || loaded.NotAfter.ToUniversalTime() <= now)
                throw new CryptographicException();
            Value = loaded;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            loaded?.Dispose();
            throw new InvalidOperationException("Data Protection requires a readable PFX with the correct password and a currently valid RSA private key.");
        }
    }

    public void Dispose() => Value.Dispose();
}

// Own this dedicated provider's services so the mounted certificate can be configured lazily,
// through supported public framework APIs, and all resources disposed with the API host.
internal sealed class HostProtectionProvider : IDataProtectionProvider, IDisposable
{
    private readonly ServiceProvider _services;
    public IKeyManager Keys => _services.GetRequiredService<IKeyManager>();

    public HostProtectionProvider(IOptions<HostKeyOptions> settings, HostCertificate certificate)
    {
        var options = settings.Value;
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(options.ApplicationName!)
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingPath!))
            .ProtectKeysWithCertificate(certificate.Value);
        _services = services.BuildServiceProvider();
    }

    public IDataProtector CreateProtector(string purpose) => _services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose);
    public void Dispose() => _services.Dispose();
}

internal sealed class KeyRingStartupCheck(HostProtectionProvider provider, IOptions<HostKeyOptions> settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var directory = settings.Value.KeyRingPath!;
            var probePath = Path.Combine(directory, ".geosurvey-write-probe-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            foreach (var file in Directory.EnumerateFiles(directory, "*.xml"))
            {
                var xml = XDocument.Load(file);
                if (xml.Root?.Name.LocalName == "key" && (!xml.Descendants().Any(element => element.Name.LocalName == "encryptedSecret")
                    || xml.Descendants().Any(element => element.Name.LocalName == "masterKey")))
                    throw new CryptographicException();
            }
            foreach (var key in provider.Keys.GetAllKeys().Where(key => !key.IsRevoked))
                if (key.CreateEncryptor() is null)
                    throw new CryptographicException();
            var protector = provider.CreateProtector("GeoSurvey.Host.KeyRingStartup.v1");
            var probe = Guid.NewGuid().ToString("N");
            if (protector.Unprotect(protector.Protect(probe)) != probe)
                throw new CryptographicException();
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException or XmlException)
        {
            throw new InvalidOperationException("Data Protection key-ring startup verification failed. Check key-directory access and the certificate required by existing keys.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
