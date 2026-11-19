using System.Security.Claims;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure.Sessions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeoSurvey.Identity.Infrastructure.Authentication;

/// <summary>RS256 issuance for this closed first-party application. No credential versions or database locators enter claims.</summary>
public sealed class RsaAccessTokenIssuer(SecurityKey signingKey, string issuer, string audience, TimeProvider clock) : IAccessTokenIssuer
{
    public AccessToken Issue(SessionSubject subject, DateTimeOffset sessionExpiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        if (subject.AccountPublicId == Guid.Empty || subject.SessionPublicId == Guid.Empty || !Enum.IsDefined(subject.Scope)
            || (subject.Scope == AuthenticationScope.Organization) != (subject.OrganizationPublicId is { } id && id != Guid.Empty))
            throw new ArgumentException("A valid current server-side subject is required.", nameof(subject));
        var now = clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero || sessionExpiresAtUtc.Offset != TimeSpan.Zero || sessionExpiresAtUtc <= now)
            throw new InvalidOperationException("An unexpired UTC session is required for access-token issuance.");
        var expiry = now.Add(PlatformSessionPolicy.AccessTokenLifetime);
        if (expiry > sessionExpiresAtUtc)
            expiry = sessionExpiresAtUtc;
        var claims = new List<Claim>
        {
            new(AuthenticationClaims.Subject, subject.AccountPublicId.ToString("D")),
            new(AuthenticationClaims.Session, subject.SessionPublicId.ToString("D")),
            new(AuthenticationClaims.Scope, subject.Scope.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AuthenticationClaims.Permission, subject.Scope == AuthenticationScope.Platform
                ? AuthenticationClaims.PlatformPermission : AuthenticationClaims.OrganizationPermission)
        };
        if (subject.OrganizationPublicId is { } organization)
            claims.Add(new(AuthenticationClaims.Organization, organization.ToString("D")));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiry.UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            TokenType = "at+jwt"
        };
        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expiry);
    }
}
