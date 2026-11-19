namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Confirmed account-bound ciphertext, never the plaintext authenticator secret.</summary>
public sealed class ConfirmedTotpCredential
{
    public int AccountId { get; private set; }
    public string ProtectedSecret { get; private set; } = null!;
    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    private ConfirmedTotpCredential() { }

    public static ConfirmedTotpCredential Create(int accountId, string protectedSecret, DateTimeOffset confirmedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedSecret);
        if (confirmedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Confirmation timestamps must be UTC.", nameof(confirmedAtUtc));
        return new ConfirmedTotpCredential { AccountId = accountId, ProtectedSecret = protectedSecret, ConfirmedAtUtc = confirmedAtUtc };
    }
}
