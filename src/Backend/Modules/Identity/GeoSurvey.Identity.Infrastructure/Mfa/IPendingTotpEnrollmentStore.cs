namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Trusted internal persistence boundary; accepts only protected secret material and token digests.</summary>
public interface IPendingTotpEnrollmentStore
{
    Task<bool> TryStoreAsync(Guid accountPublicId, string enrollmentTokenHash, string protectedSecret,
        CancellationToken cancellationToken);

    Task<string?> ReadProtectedSecretAsync(Guid accountPublicId, string enrollmentTokenHash,
        DateTimeOffset now, CancellationToken cancellationToken);
}
