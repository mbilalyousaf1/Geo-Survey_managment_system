namespace GeoSurvey.Identity.Infrastructure.SignIn;

/// <summary>Starts session storage from real password-bound MFA proof and owns its commit.</summary>
public interface IPlatformSessionStarter
{
    Task<PlatformSessionStartResult> StartAsync(string? verificationToken, string? code, CancellationToken cancellationToken = default);
}

/// <summary>Committed storage result, not an access token/principal. Never log or destructure its ticket.</summary>
public sealed class PlatformSessionStartResult
{
    public bool SessionCreated => Ticket is not null;
    public PlatformSessionTicket? Ticket { get; }

    private PlatformSessionStartResult(PlatformSessionTicket? ticket) => Ticket = ticket;

    public static PlatformSessionStartResult Rejected { get; } = new(null);

    public static PlatformSessionStartResult Started(PlatformSessionTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new PlatformSessionStartResult(ticket);
    }
}

/// <summary>Server-only credential delivery payload. Do not log, trace, or serialize it as diagnostics.</summary>
public sealed class PlatformSessionTicket
{
    public Guid AccountPublicId { get; }
    public Guid SessionPublicId { get; }
    public string RefreshToken { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset RefreshExpiresAtUtc { get; }
    public DateTimeOffset SessionExpiresAtUtc { get; }

    public PlatformSessionTicket(Guid accountPublicId, Guid sessionPublicId, string refreshToken,
        DateTimeOffset createdAtUtc, DateTimeOffset refreshExpiresAtUtc, DateTimeOffset sessionExpiresAtUtc)
    {
        if (accountPublicId == Guid.Empty || sessionPublicId == Guid.Empty)
            throw new ArgumentException("Public account and session identifiers are required.");
        if (refreshToken is not { Length: 43 }
            || refreshToken.Any(character => character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("An opaque refresh token is required.", nameof(refreshToken));
        if (createdAtUtc.Offset != TimeSpan.Zero || refreshExpiresAtUtc.Offset != TimeSpan.Zero || sessionExpiresAtUtc.Offset != TimeSpan.Zero
            || refreshExpiresAtUtc <= createdAtUtc || sessionExpiresAtUtc <= createdAtUtc || refreshExpiresAtUtc > sessionExpiresAtUtc)
            throw new ArgumentException("Session ticket timestamps must be UTC with valid bounded expiry.");

        AccountPublicId = accountPublicId;
        SessionPublicId = sessionPublicId;
        RefreshToken = refreshToken;
        CreatedAtUtc = createdAtUtc;
        RefreshExpiresAtUtc = refreshExpiresAtUtc;
        SessionExpiresAtUtc = sessionExpiresAtUtc;
    }
}
