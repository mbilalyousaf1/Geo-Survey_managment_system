# Phase 1 public authentication

Implemented within the owner's approved [submission delivery batch](phase-1-submission-delivery-plan.md). HTTP contracts are maintained in [integration contracts](../architecture/phase-1-integration-contracts.md). This document explains the authentication implementation; the submission validation report records the final combined verification.

## Flow and ownership

The Identity Application project provides small framework-free authentication contracts. Identity Infrastructure supplies opaque-token generation/formatting, claim names, and actual RS256 access-token issuance. ControlPlane Infrastructure owns database transactions and reuses the accepted password, challenge, enrollment, TOTP, replay, recovery-code, and session stores. The API handles HTTP bodies, secure cookies, CSRF, rate limits, signed-token validation, and fixed public errors.

Platform password verification returns only a restricted stored Enrollment or Verification challenge. Enrollment resolves the account from that opaque challenge, generates the authenticator secret on the server, stores account-bound protected ciphertext, and returns authenticator provisioning material. Repeating material retrieval for the same valid challenge returns the same pending secret. Confirmation atomically enables MFA and returns ten recovery codes once. The browser then starts a fresh password entry; confirmation does not silently create a session. A TOTP step accepted for enrollment cannot also be accepted for sign-in: wait for a fresh authenticator code when completing the manual flow.

Platform sign-in accepts a password-bound Verification challenge plus TOTP, or that challenge plus one unused recovery code. The recovery path normalizes surrounding whitespace and invariant uppercase only, then enforces the issued eight-group hexadecimal format. Recovery and TOTP share the accepted five-failure/15-minute MFA budget. Challenge consumption, backup consumption, counter changes, and session persistence commit together. Recovery preserves the confirmed authenticator and last accepted TOTP step.

Organization sign-in resolves organization code plus normalized email and verifies that organization's independent password hash. Five wrong passwords trigger the same 15-minute password lockout selected for Phase 1. A valid password creates a session only when `ITenantAdministratorAccess` verifies the current Ready registry, database identity, and actual administrator grant. Current organization access and every refresh check that contract again; no HTTP database selector is accepted.

## Credentials and current authorization

Access tokens last at most five minutes and are capped by the original eight-hour absolute session deadline. Refresh credentials expire after 30 minutes, with replacements capped by that same absolute deadline. Raw refresh values are returned only to the API's host-only `__Host-GeoSurvey.Platform.Refresh` or `__Host-GeoSurvey.Organization.Refresh` cookie. Cookies use Path=/, Secure, HttpOnly, and SameSite=Strict. JSON responses never contain refresh credentials or security stamps; the web application keeps access tokens in memory.

Strict rotation retains consumed digest records. A second use of a consumed token revokes its entire family, including the concurrently issued replacement; there is no grace interval. Account-first/family/token update locks serialize these decisions. Failed pre-commit writes roll back consumption. Logout revokes the cookie's family. Optional expected session IDs on refresh/logout are consistency checks only: a mismatch returns 409 and leaves the newer cookie/family intact. Clients must coordinate browser refreshes; an uncertain network/commit result requires fresh sign-in rather than transparent raw-token retries.

JWT validation checks RS256 signature, HTTPS issuer, audience, token type, exact expiry with zero skew, public subject/session/scope/organization bindings, and the current account/family/security-stamp binding. Platform claims must still correspond to the current administrator grant and confirmed MFA. Revoked families, password/security-stamp changes, current lockouts, removed roles, and removed tenant access invalidate authorization immediately. Platform sessions cannot satisfy the OrganizationAdministrator policy.

All unsafe authentication routes validate an anonymous CSRF binding from `GET /api/v1/auth/csrf` and `X-CSRF-TOKEN`; this remains stable across sign-in identity changes. A per-IP fixed-window limiter permits 30 auth requests per minute. Authentication errors use fixed ProblemDetails without provider details, credential values, or stored material. Public OpenAPI describes bearer and CSRF security requirements.

## Host configuration and dependencies

Configure Authentication:Issuer, Audience, SigningCertificatePath, and SigningCertificatePassword. The signing PFX must exist outside the application/repository, contain a currently valid RSA private key of at least 2048 bits, and use a different key from Data Protection. Startup rejects the same RSA key even if supplied through another certificate file. Supply certificate passwords through environment/secret configuration; retain the separately configured persistent Data Protection key ring.

New dependencies have specific purposes:

- Microsoft.AspNetCore.Authentication.JwtBearer 10.0.9 implements standard ASP.NET Core bearer validation and HTTP authentication challenges.
- Microsoft.IdentityModel.JsonWebTokens 8.23.0 implements standards-based JWT issuance using the mounted RSA key. Its current 8.x line supports .NET 10. See [official package](https://www.nuget.org/packages/Microsoft.IdentityModel.JsonWebTokens/8.23.0).
- Microsoft.AspNetCore.OpenApi 10.0.9 generates HTTP API documentation. Microsoft.OpenApi is explicitly pinned to compatible 2.7.5 because ASP.NET's minimum 2.0.0 transitive dependency is affected by CVE-2026-49451; 2.7.5 is patched. See [Microsoft's advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc).

These do not introduce an external identity provider or package-based TOTP implementation. The closed first-party application uses the session policy explicitly selected in the approved plan.

## Targeted verification

Commands executed during implementation:

- `dotnet build src/Backend/GeoSurvey.Api/GeoSurvey.Api.csproj --configuration Release --no-restore --nologo -m:1` passed; a cached Tenancy NuGet audit lookup produced NU1900 during restricted-network execution.
- `dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests/GeoSurvey.Identity.Infrastructure.Tests.csproj --configuration Release --nologo -m:1` passed 202/202, including real RSA signature/issuer/audience/expiry and recovery-format checks.
- `dotnet test tests/Integration/GeoSurvey.Api.IntegrationTests/GeoSurvey.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~AuthenticationHttpSecurityTests --nologo` passed 7/7, including CSRF, secure host cookie, anonymous/malformed bearer rejection, signing/DP key separation, and generated OpenAPI checks.
- `dotnet test tests/Integration/GeoSurvey.Api.IntegrationTests/GeoSurvey.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName!~PlatformAuthenticationAcceptanceTests --nologo` passed all 26 offline API tests.
- `dotnet build tests/Integration/GeoSurvey.ControlPlane.Infrastructure.IntegrationTests/GeoSurvey.ControlPlane.Infrastructure.IntegrationTests.csproj --configuration Release --no-restore --nologo -m:1` passed with zero warnings/errors. The corresponding API integration-project build also passed, with the cached Tenancy NU1900 audit warning.
- Scoped `dotnet format whitespace GeoSurvey.sln --no-restore --include ... --verbosity minimal` completed. Restricted execution initially failed connecting to its local MSBuild named pipe; the same scoped formatter completed with the required local-process permission. This formatting result is distinct from application tests.

The root agent's initial coordinated `scripts/Test-Backend.ps1` run passed all 27 API tests, including the actual SQL/HTTP Platform acceptance test. That test exercises the operator bootstrap component through enrollment, TOTP sign-in, real JWT authorization, scope denial, recovery, rotation/reuse, and logout. The same run reported no authentication lifecycle failures in the control-plane SQL project; a separate existing bootstrap elapsed-time assertion failed. This is not a claim that the whole initial suite passed.

The final authentication lifecycle file has eleven SQL tests covering recovery single use and shared budgeting, rollback, concurrent refresh reuse, session consistency, logout, stamp changes, expiry, organization scope/current grants, and challenge-routed enrollment. Three final tests add replacement-write rollback, the fixed eight-hour deadline across repeated refreshes, and legacy future-lockout enrollment denial. The final logout session-consistency guard and these last additions require the final combined rerun; consult the submission validation report for that result. No documentation-only check substitutes for application tests.
