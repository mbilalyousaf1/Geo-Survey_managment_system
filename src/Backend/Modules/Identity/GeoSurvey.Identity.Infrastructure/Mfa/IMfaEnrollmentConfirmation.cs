namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Trusted internal enrollment flow. Account selection and initial password verification are server responsibilities.</summary>
public interface IMfaEnrollmentConfirmation
{
    Task<MfaEnrollmentConfirmationResult> ConfirmAsync(Guid accountPublicId, string? enrollmentToken, string? code, CancellationToken cancellationToken = default);
}

/// <summary>Not a record: no generated secret-bearing ToString. Never log/destructure this result.</summary>
public sealed class MfaEnrollmentConfirmationResult
{
    public bool Succeeded { get; }
    public IReadOnlyList<string> RecoveryCodes { get; }

    private MfaEnrollmentConfirmationResult(bool succeeded, IReadOnlyList<string> recoveryCodes)
    {
        Succeeded = succeeded;
        RecoveryCodes = recoveryCodes;
    }

    public static MfaEnrollmentConfirmationResult Rejected { get; } = new(false, Array.Empty<string>());

    public static MfaEnrollmentConfirmationResult Confirmed(IReadOnlyList<string> recoveryCodes)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        if (recoveryCodes.Count != MfaRecoveryCodes.BatchSize)
            throw new ArgumentException("A complete initial recovery-code batch is required.", nameof(recoveryCodes));
        return new MfaEnrollmentConfirmationResult(true, Array.AsReadOnly(recoveryCodes.ToArray()));
    }
}
