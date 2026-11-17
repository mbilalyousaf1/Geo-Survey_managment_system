namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Unconfirmed ciphertext only. This row does not enable MFA or grant access.</summary>
public sealed class PendingTotpEnrollment
{
    public int AccountId { get; private set; }
    public int ChallengeId { get; private set; }
    public string ProtectedSecret { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private PendingTotpEnrollment() { }

    public static PendingTotpEnrollment Create(int accountId, int challengeId, string protectedSecret, DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(challengeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Pending enrollment timestamps must be UTC.", nameof(createdAtUtc));
        return new PendingTotpEnrollment
        {
            AccountId = accountId,
            ChallengeId = challengeId,
            ProtectedSecret = protectedSecret,
            CreatedAtUtc = createdAtUtc
        };
    }
}
