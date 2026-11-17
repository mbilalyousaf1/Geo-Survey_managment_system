namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Ephemeral challenge storage; contains only a token digest, never its raw value.</summary>
public sealed class MfaChallenge
{
    public int Id { get; private set; }
    public int AccountId { get; private set; }
    public MfaChallengePurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string SecurityStamp { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private MfaChallenge() { }

    public static MfaChallenge Create(int accountId, MfaChallengePurpose purpose, string tokenHash,
        string securityStamp, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (tokenHash is null || tokenHash.Length != 64 || tokenHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 token digest is required.", nameof(tokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Challenge timestamps must be UTC with expiry after creation.");

        return new MfaChallenge
        {
            AccountId = accountId,
            Purpose = purpose,
            TokenHash = tokenHash.ToUpperInvariant(),
            SecurityStamp = securityStamp,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
