namespace GeoSurvey.Identity.Application.Authentication;

public enum AuthenticationScope { Platform, Organization }

/// <summary>A currently authorized public subject, resolved entirely from server-side state.</summary>
public sealed record SessionSubject(Guid AccountPublicId, Guid SessionPublicId, AuthenticationScope Scope,
    Guid? OrganizationPublicId, string Email, string? OrganizationCode = null, string? OrganizationName = null);

/// <summary>Credential delivery only. Never log/destructure this object.</summary>
public sealed class SessionGrant(SessionSubject subject, string refreshToken, DateTimeOffset refreshExpiresAtUtc, DateTimeOffset sessionExpiresAtUtc)
{
    public SessionSubject Subject { get; } = subject;
    public string RefreshToken { get; } = refreshToken;
    public DateTimeOffset RefreshExpiresAtUtc { get; } = refreshExpiresAtUtc;
    public DateTimeOffset SessionExpiresAtUtc { get; } = sessionExpiresAtUtc;
}

public interface IAuthenticationSessions
{
    Task<SessionSubject?> ValidateAsync(Guid accountPublicId, Guid sessionPublicId, AuthenticationScope scope,
        Guid? organizationPublicId, CancellationToken cancellationToken = default);
    Task<SessionGrant?> RotateAsync(string? refreshToken, AuthenticationScope scope, Guid? expectedSessionPublicId = null, CancellationToken cancellationToken = default);
    Task RevokeAsync(string? refreshToken, AuthenticationScope scope, Guid? expectedSessionPublicId = null, CancellationToken cancellationToken = default);
    Task<SessionGrant?> SignInOrganizationAsync(string? organizationCode, string? email, string? password, CancellationToken cancellationToken = default);
    Task<SessionGrant?> SignInPlatformRecoveryAsync(string? verificationToken, string? recoveryCode, CancellationToken cancellationToken = default);
}

public sealed class AuthenticationSessionChangedException() : Exception("The browser session changed. Sign in again to continue.");

public interface IAccessTokenIssuer
{
    AccessToken Issue(SessionSubject subject, DateTimeOffset sessionExpiresAtUtc);
}

/// <summary>Never log or trace a bearer token.</summary>
public sealed class AccessToken(string value, DateTimeOffset expiresAtUtc)
{
    public string Value { get; } = value;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}

public interface IPlatformEnrollment
{
    Task<EnrollmentMaterial?> BeginAsync(string? enrollmentToken, CancellationToken cancellationToken = default);
    Task<EnrollmentConfirmation?> ConfirmAsync(string? enrollmentToken, string? code, CancellationToken cancellationToken = default);
}

/// <summary>Secret delivery only; its default string representation contains no material.</summary>
public sealed class EnrollmentMaterial(string secret, string authenticatorUri, DateTimeOffset expiresAtUtc)
{
    public string Secret { get; } = secret;
    public string AuthenticatorUri { get; } = authenticatorUri;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}

public sealed class EnrollmentConfirmation(IReadOnlyList<string> recoveryCodes)
{
    public IReadOnlyList<string> RecoveryCodes { get; } = recoveryCodes;
}
