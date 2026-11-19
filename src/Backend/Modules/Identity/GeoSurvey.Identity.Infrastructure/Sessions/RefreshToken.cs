namespace GeoSurvey.Identity.Infrastructure.Sessions;

/// <summary>Refresh-token digest and consumption evidence; never stores the raw bearer token.</summary>
public sealed class RefreshToken
{
    public long Id { get; private set; }
    public long TokenFamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(long tokenFamilyId, string tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenFamilyId);
        if (tokenHash is null || tokenHash.Length != 64 || tokenHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 refresh-token digest is required.", nameof(tokenHash));
        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero || expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Refresh-token timestamps must be UTC with expiry after creation.");

        return new RefreshToken
        {
            TokenFamilyId = tokenFamilyId,
            TokenHash = tokenHash.ToUpperInvariant(),
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
