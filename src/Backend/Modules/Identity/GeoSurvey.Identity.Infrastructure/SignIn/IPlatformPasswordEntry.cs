using GeoSurvey.Identity.Infrastructure.Mfa;

namespace GeoSurvey.Identity.Infrastructure.SignIn;

/// <summary>Dedicated platform-scope entry. Password success permits only the next restricted MFA step.</summary>
public interface IPlatformPasswordEntry
{
    Task<PlatformPasswordEntryResult> BeginAsync(string? email, string? password, CancellationToken cancellationToken = default);
}

public static class PlatformPasswordPolicy
{
    public const int FailureLimit = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}

/// <summary>Not an authenticated session or a record. Never log/destructure a challenge-bearing result.</summary>
public sealed class PlatformPasswordEntryResult
{
    public bool ChallengeIssued => Challenge is not null;
    public MfaChallengePurpose? Purpose { get; }
    public MfaChallengeTicket? Challenge { get; }

    private PlatformPasswordEntryResult(MfaChallengePurpose? purpose, MfaChallengeTicket? challenge)
    {
        Purpose = purpose;
        Challenge = challenge;
    }

    public static PlatformPasswordEntryResult Rejected { get; } = new(null, null);

    public static PlatformPasswordEntryResult Issued(MfaChallengePurpose purpose, MfaChallengeTicket challenge)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        ArgumentNullException.ThrowIfNull(challenge);
        return new PlatformPasswordEntryResult(purpose, challenge);
    }
}
