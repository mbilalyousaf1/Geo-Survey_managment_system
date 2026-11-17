using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Trusted enrollment workflow only. Does not generate secrets, confirm codes, enable MFA, or issue tokens.</summary>
public sealed class PendingTotpEnrollmentService(IPendingTotpEnrollmentStore store, ITotpSecretProtector protector, TimeProvider clock)
{
    /// <summary>Secret must come from the future trusted enrollment generator, never arbitrary HTTP input.</summary>
    public Task<bool> TryStoreAsync(Guid accountPublicId, string? enrollmentToken, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (accountPublicId == Guid.Empty || !IsWellFormed(enrollmentToken))
            return Task.FromResult(false);
        cancellationToken.ThrowIfCancellationRequested();
        var ciphertext = protector.Protect(accountPublicId, secret);
        return store.TryStoreAsync(accountPublicId, Hash(enrollmentToken!), ciphertext, cancellationToken);
    }

    /// <summary>Returns a secret only to the trusted verification workflow while its enrollment challenge is valid.</summary>
    public async Task<string?> ReadSecretAsync(Guid accountPublicId, string? enrollmentToken, CancellationToken cancellationToken = default)
    {
        if (accountPublicId == Guid.Empty || !IsWellFormed(enrollmentToken))
            return null;
        var ciphertext = await store.ReadProtectedSecretAsync(accountPublicId, Hash(enrollmentToken!), clock.GetUtcNow(), cancellationToken);
        return ciphertext is null ? null : protector.Unprotect(accountPublicId, ciphertext);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool IsWellFormed(string? token) => token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');
}
