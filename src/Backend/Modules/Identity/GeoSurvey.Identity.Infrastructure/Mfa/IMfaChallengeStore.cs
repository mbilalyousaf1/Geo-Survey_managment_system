namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Narrow global persistence capability; no context or connection crosses this boundary.</summary>
public interface IMfaChallengeStore
{
    Task<bool> ReplaceAsync(Guid accountPublicId, string verifiedSecurityStamp, MfaChallengePurpose purpose,
        string tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
    Task<bool> IsValidAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TryConsumeAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash,
        DateTimeOffset now, CancellationToken cancellationToken);
}
