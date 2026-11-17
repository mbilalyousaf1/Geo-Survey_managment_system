namespace GeoSurvey.Identity.Infrastructure.Mfa;

public interface IMfaVerificationStore
{
    Task<MfaFailureResult> RecordFailureAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, CancellationToken cancellationToken);

    /// <summary>Requires a caller-owned transaction. A trusted verifier supplies the matched step, never a client.</summary>
    Task<bool> TryAcceptStepAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, long matchedStep, CancellationToken cancellationToken);
}
