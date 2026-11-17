using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Challenge lifecycle only: caller must already have verified credentials/MFA as appropriate.</summary>
public sealed class MfaChallengeService(IMfaChallengeStore store, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Trusted password-verification flow supplies the account and stamp it verified, never client assertions.</summary>
    public async Task<MfaChallengeTicket> IssueAsync(Guid accountPublicId, string verifiedSecurityStamp,
        MfaChallengePurpose purpose, CancellationToken cancellationToken = default)
    {
        ValidatePurpose(purpose);
        if (accountPublicId == Guid.Empty)
            throw new ArgumentException("A verified account is required.", nameof(accountPublicId));
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSecurityStamp);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var created = clock.GetUtcNow();
        var expires = created.Add(Lifetime);
        if (!await store.ReplaceAsync(accountPublicId, verifiedSecurityStamp, purpose, Hash(token), created, expires, cancellationToken))
            throw new InvalidOperationException("Account is not eligible for the requested MFA challenge.");
        return new MfaChallengeTicket(token, expires);
    }

    public Task<bool> IsValidAsync(Guid accountPublicId, MfaChallengePurpose purpose, string? token, CancellationToken cancellationToken = default)
    {
        ValidatePurpose(purpose);
        return IsWellFormed(token) && accountPublicId != Guid.Empty
            ? store.IsValidAsync(accountPublicId, purpose, Hash(token!), clock.GetUtcNow(), cancellationToken)
            : Task.FromResult(false);
    }

    /// <summary>Only the trusted successful MFA-completion flow may call this. True means nonce consumption, not authentication.</summary>
    public Task<bool> TryConsumeAsync(Guid accountPublicId, MfaChallengePurpose purpose, string? token, CancellationToken cancellationToken = default)
    {
        ValidatePurpose(purpose);
        return IsWellFormed(token) && accountPublicId != Guid.Empty
            ? store.TryConsumeAsync(accountPublicId, purpose, Hash(token!), clock.GetUtcNow(), cancellationToken)
            : Task.FromResult(false);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool IsWellFormed(string? token) => token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    private static void ValidatePurpose(MfaChallengePurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
    }
}

/// <summary>Raw token is returned only to the issuing flow. Not a record: no generated token-bearing ToString.</summary>
public sealed class MfaChallengeTicket(string token, DateTimeOffset expiresAtUtc)
{
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}
