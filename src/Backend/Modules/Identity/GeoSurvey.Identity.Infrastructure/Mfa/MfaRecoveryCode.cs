namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Account-bound digest only. Recovery authentication/consumption is a separate reviewed workflow.</summary>
public sealed class MfaRecoveryCode
{
    public int AccountId { get; private set; }
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    private MfaRecoveryCode() { }

    public static MfaRecoveryCode Create(int accountId, string codeHash, DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId);
        if (codeHash is null || codeHash.Length != 64 || codeHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 recovery-code digest is required.", nameof(codeHash));
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Recovery-code timestamps must be UTC.", nameof(createdAtUtc));
        return new MfaRecoveryCode { AccountId = accountId, CodeHash = codeHash.ToUpperInvariant(), CreatedAtUtc = createdAtUtc };
    }
}
