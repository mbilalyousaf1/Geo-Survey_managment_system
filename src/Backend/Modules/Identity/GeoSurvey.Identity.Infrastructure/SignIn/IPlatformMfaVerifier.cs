namespace GeoSurvey.Identity.Infrastructure.SignIn;

/// <summary>Trusted verification step in a caller-owned read-committed completion transaction.</summary>
public interface IPlatformMfaVerifier
{
    Task<PlatformMfaVerificationResult> VerifyAsync(string? verificationToken, string? code, CancellationToken cancellationToken = default);
}

/// <summary>Server-only provisional subject. Discard on rollback; never accept this result from a client as proof.</summary>
public sealed class PlatformMfaVerificationResult
{
    public bool IsVerified => AccountPublicId is not null;
    public Guid? AccountPublicId { get; }

    private PlatformMfaVerificationResult(Guid? accountPublicId) => AccountPublicId = accountPublicId;

    public static PlatformMfaVerificationResult Rejected { get; } = new(null);

    public static PlatformMfaVerificationResult Verified(Guid accountPublicId)
    {
        if (accountPublicId == Guid.Empty)
            throw new ArgumentException("A verified account public identifier is required.", nameof(accountPublicId));
        return new PlatformMfaVerificationResult(accountPublicId);
    }
}
