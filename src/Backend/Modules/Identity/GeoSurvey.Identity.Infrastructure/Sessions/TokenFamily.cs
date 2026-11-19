namespace GeoSurvey.Identity.Infrastructure.Sessions;

/// <summary>Account-bound session storage. Its presence or public identifier is not authentication proof.</summary>
public sealed class TokenFamily
{
    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public int AccountId { get; private set; }
    public string SecurityStamp { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private TokenFamily() { }

    public static TokenFamily Create(int accountId, string securityStamp, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Session timestamps must be UTC with expiry after creation.");

        return new TokenFamily
        {
            PublicId = Guid.NewGuid(),
            AccountId = accountId,
            SecurityStamp = securityStamp,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
