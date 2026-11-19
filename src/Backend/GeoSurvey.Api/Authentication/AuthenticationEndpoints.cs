using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure.SignIn;
using Microsoft.AspNetCore.Antiforgery;

namespace GeoSurvey.Api.Authentication;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapGeoSurveyAuthentication(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            NoStore(context);
            // A stable anonymous binding permits the same browser CSRF flow before and after sign-in.
            var principal = context.User;
            try
            {
                context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
                var tokens = antiforgery.GetAndStoreTokens(context);
                return Results.Ok(new { data = new { requestToken = tokens.RequestToken } });
            }
            finally { context.User = principal; }
        }).AllowAnonymous().RequireRateLimiting("authentication").WithName("GetAuthenticationCsrf");

        var flow = group.MapGroup("").AllowAnonymous().RequireRateLimiting("authentication")
            .AddEndpointFilter<AuthenticationEndpointFilter>();
        flow.MapPost("/platform/password", async (PasswordRequest request, IPlatformPasswordEntry entry, HttpContext context) =>
        {
            var result = await entry.BeginAsync(request.Email, request.Password, context.RequestAborted);
            return result.ChallengeIssued
                ? Results.Ok(new { data = new { step = result.Purpose!.Value.ToString(), challengeToken = result.Challenge!.Token, result.Challenge.ExpiresAtUtc } })
                : Rejected(context);
        }).WithName("PlatformPasswordEntry");
        flow.MapPost("/platform/enrollment", async (ChallengeRequest request, IPlatformEnrollment enrollment, HttpContext context) =>
        {
            var result = await enrollment.BeginAsync(request.ChallengeToken, context.RequestAborted);
            return result is null ? Rejected(context)
                : Results.Ok(new { data = new { result.Secret, provisioningUri = result.AuthenticatorUri, result.ExpiresAtUtc } });
        }).WithName("PlatformAuthenticatorEnrollment");
        flow.MapPost("/platform/enrollment/confirm", async (CodeRequest request, IPlatformEnrollment enrollment, HttpContext context) =>
        {
            var result = await enrollment.ConfirmAsync(request.ChallengeToken, request.Code, context.RequestAborted);
            return result is null ? Rejected(context) : Results.Ok(new { data = new { result.RecoveryCodes } });
        }).WithName("ConfirmPlatformAuthenticator");
        flow.MapPost("/platform/verify", async (CodeRequest request, IPlatformSessionStarter starter,
            IAuthenticationSessions sessions, IAccessTokenIssuer issuer, HttpContext context) =>
        {
            var result = await starter.StartAsync(request.ChallengeToken, request.Code, context.RequestAborted);
            if (result.Ticket is not { } ticket)
                return Rejected(context);
            var subject = await sessions.ValidateAsync(ticket.AccountPublicId, ticket.SessionPublicId, AuthenticationScope.Platform, null, context.RequestAborted);
            return subject is null ? Rejected(context) : Deliver(context, issuer,
                new SessionGrant(subject, ticket.RefreshToken, ticket.RefreshExpiresAtUtc, ticket.SessionExpiresAtUtc));
        }).WithName("VerifyPlatformAuthenticator");
        flow.MapPost("/platform/recovery", async (RecoveryRequest request, IAuthenticationSessions sessions, IAccessTokenIssuer issuer, HttpContext context) =>
        {
            var grant = await sessions.SignInPlatformRecoveryAsync(request.ChallengeToken, request.RecoveryCode, context.RequestAborted);
            return grant is null ? Rejected(context) : Deliver(context, issuer, grant);
        }).WithName("PlatformRecoverySignIn");
        flow.MapPost("/organization/password", async (OrganizationPasswordRequest request, IAuthenticationSessions sessions, IAccessTokenIssuer issuer, HttpContext context) =>
        {
            var grant = await sessions.SignInOrganizationAsync(request.OrganizationCode, request.Email, request.Password, context.RequestAborted);
            return grant is null ? Rejected(context) : Deliver(context, issuer, grant);
        }).WithName("OrganizationPasswordEntry");

        foreach (var scope in new[] { AuthenticationScope.Platform, AuthenticationScope.Organization })
        {
            var capturedScope = scope;
            var scopePath = scope == AuthenticationScope.Platform ? "platform" : "organization";
            flow.MapPost($"/{scopePath}/refresh", async (RefreshRequest? request, IAuthenticationSessions sessions, IAccessTokenIssuer issuer, HttpContext context) =>
            {
                var grant = await sessions.RotateAsync(context.Request.Cookies[CookieName(capturedScope)], capturedScope,
                    request?.ExpectedSessionPublicId, context.RequestAborted);
                if (grant is not null)
                    return Deliver(context, issuer, grant);
                context.Response.Cookies.Delete(CookieName(capturedScope), RefreshCookie());
                return Rejected(context);
            }).WithName(scope + "Refresh");
            flow.MapPost($"/{scopePath}/logout", async (RefreshRequest? request, IAuthenticationSessions sessions, HttpContext context) =>
            {
                await sessions.RevokeAsync(context.Request.Cookies[CookieName(capturedScope)], capturedScope, request?.ExpectedSessionPublicId, context.RequestAborted);
                context.Response.Cookies.Delete(CookieName(capturedScope), RefreshCookie());
                return Results.NoContent();
            }).WithName(scope + "Logout");
        }
        group.MapGet("/me", (HttpContext context) =>
        {
            NoStore(context);
            return context.CurrentSession() is { } subject
                ? Results.Ok(new { data = Profile(subject) }) : Rejected(context);
        }).RequireAuthorization().WithName("CurrentAuthenticatedUser");
        return endpoints;
    }

    private static IResult Deliver(HttpContext context, IAccessTokenIssuer issuer, SessionGrant grant)
    {
        var token = issuer.Issue(grant.Subject, grant.SessionExpiresAtUtc);
        var cookie = RefreshCookie();
        cookie.Expires = grant.RefreshExpiresAtUtc;
        context.Response.Cookies.Append(CookieName(grant.Subject.Scope), grant.RefreshToken, cookie);
        return Results.Ok(new
        {
            data = new SessionResponse
            {
                AccessToken = token.Value,
                ExpiresAtUtc = token.ExpiresAtUtc,
                SessionExpiresAtUtc = grant.SessionExpiresAtUtc,
                SessionPublicId = grant.Subject.SessionPublicId,
                User = Profile(grant.Subject)
            }
        });
    }

    private static object Profile(SessionSubject subject) => new
    {
        subject.AccountPublicId,
        subject.Email,
        scope = subject.Scope.ToString(),
        subject.OrganizationPublicId,
        subject.OrganizationCode,
        subject.OrganizationName
    };

    private static CookieOptions RefreshCookie() => new()
    { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true };

    public static string CookieName(AuthenticationScope scope) => scope == AuthenticationScope.Platform
        ? "__Host-GeoSurvey.Platform.Refresh" : "__Host-GeoSurvey.Organization.Refresh";

    private static IResult Rejected(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: 401, title: "The sign-in proof is invalid, expired, or no longer available.");
    }

    private static void NoStore(HttpContext context)
    { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache"; }

    public sealed class PasswordRequest { public string? Email { get; set; } public string? Password { get; set; } }
    public sealed class OrganizationPasswordRequest
    { public string? OrganizationCode { get; set; } public string? Email { get; set; } public string? Password { get; set; } }
    public sealed class ChallengeRequest { public string? ChallengeToken { get; set; } }
    public sealed class CodeRequest { public string? ChallengeToken { get; set; } public string? Code { get; set; } }
    public sealed class RecoveryRequest { public string? ChallengeToken { get; set; } public string? RecoveryCode { get; set; } }
    public sealed class RefreshRequest { public Guid? ExpectedSessionPublicId { get; set; } }
    private sealed class SessionResponse
    {
        public required string AccessToken { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public required DateTimeOffset SessionExpiresAtUtc { get; init; }
        public required Guid SessionPublicId { get; init; }
        public required object User { get; init; }
    }
}
