namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Account-level control state; never contains codes, secrets, or authentication grants.</summary>
public sealed class MfaVerificationState
{
    public int AccountId { get; private set; }
    public int FailedAttemptCount { get; private set; }
    public DateTimeOffset? FailureWindowStartedAtUtc { get; private set; }
    public DateTimeOffset? BlockedUntilUtc { get; private set; }
    public long? LastAcceptedStep { get; private set; }

    private MfaVerificationState() { }
}

public static class MfaAttemptPolicy
{
    public const int FailureLimit = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);
}

public enum MfaFailureResult
{
    InvalidChallenge,
    FailureRecorded,
    Blocked
}
