using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Host owns durable key storage, encryption at rest, and environment isolation.</summary>
public sealed class DataProtectionTotpSecretProtector : ITotpSecretProtector
{
    private readonly IDataProtectionProvider _provider;
    private const string Purpose = "GeoSurvey.Identity.TotpSecret.v1";

    public DataProtectionTotpSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public string Protect(Guid accountPublicId, string secret)
    {
        ValidateAccount(accountPublicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return ForAccount(accountPublicId).Protect(secret);
    }

    public string Unprotect(Guid accountPublicId, string protectedSecret)
    {
        ValidateAccount(accountPublicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        try
        {
            return ForAccount(accountPublicId).Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            // Do not propagate provider diagnostics or input through authentication errors/logs.
            throw new CryptographicException("Authenticator secret could not be decrypted.");
        }
    }

    private IDataProtector ForAccount(Guid accountPublicId) =>
        _provider.CreateProtector(Purpose, accountPublicId.ToString("N"));

    private static void ValidateAccount(Guid accountPublicId)
    {
        if (accountPublicId == Guid.Empty)
            throw new ArgumentException("An account public identifier is required.", nameof(accountPublicId));
    }
}
