# Phase 1 integration contracts

Accepted implementation coordination, 2026-11-18, within the owner's approved [delivery batch](../development/phase-1-submission-delivery-plan.md). Shared API agreement for the parallel work. Use camelCase JSON. Success bodies are `{ "data": ... }`; errors are sanitized ProblemDetails with an optional safe code. A 204 response has no body. Server inputs never accept a connection string, database name, internal integer ID, authorization success flag, or trusted tenant assertion.

## Authentication

All routes are under /api/v1. Auth scope strings in responses/JWT are `Platform` and `Organization`. Step strings are `Enrollment` and `Verification`. Resource routes require a Bearer token; auth flow routes use their real proof and anonymous CSRF validation. `/auth/me` is the exception under /auth: it requires Bearer authentication. Browser refresh material is only a secure HttpOnly host cookie, never JSON/localStorage.

| Route | Input | Success data |
| --- | --- | --- |
| GET /auth/csrf | None | `{requestToken}`; browser sends X-CSRF-TOKEN on subsequent unsafe auth/activation operations |
| POST /auth/platform/password | `{email,password}` | `{step,challengeToken,expiresAtUtc}` |
| POST /auth/platform/enrollment | `{challengeToken}` | `{secret,provisioningUri,expiresAtUtc}` |
| POST /auth/platform/enrollment/confirm | `{challengeToken,code}` | `{recoveryCodes:[...]}`; fresh password entry follows before administration |
| POST /auth/platform/verify | `{challengeToken,code}` | Session response below; sets Platform refresh cookie |
| POST /auth/platform/recovery | `{challengeToken,recoveryCode}` | Same session response/cookie |
| POST /auth/organization/password | `{organizationCode,email,password}` | Same session response; sets Organization refresh cookie |
| POST /auth/platform/refresh or /auth/organization/refresh | `{expectedSessionPublicId?:GUID}` | Same session response; rotates the corresponding cookie. Optional expected ID is only a consistency check against the cookie's server-resolved family, never authorization. Mismatch returns 409. |
| POST /auth/platform/logout or /auth/organization/logout | Optional `{expectedSessionPublicId:GUID}` | 204; revoke current family and clear corresponding cookie. A stale-tab expected ID returns 409 without revoking/clearing a newer session. |
| GET /auth/me | Bearer | User profile below |

Session data: `{accessToken,expiresAtUtc,sessionExpiresAtUtc,sessionPublicId,user}`.

User profile: `{accountPublicId,email,scope,organizationPublicId:null|GUID,organizationCode:null|string,organizationName:null|string}`. Platform profile organization fields are null. No raw refresh token/stamp/integer ID is exposed. A replaced browser account/session must not silently populate the old workspace.

JWT uses sub (public account GUID), sid (public family GUID), scope, and organizationId (public GUID only when Organization). Policy names are PlatformAdministrator and OrganizationAdministrator. Validate current server account/stamp/family and current tenant grant as well as the JWT signature/issuer/audience/expiry. Authentication and resource authorization errors use 401/403 without provider details.

Config: Authentication:Issuer, Audience, SigningCertificatePath, SigningCertificatePassword. Signing PFX is separate from Data Protection, RSA at least 2048 bits; demo generator uses 3072. Cookie names: __Host-GeoSurvey.Platform.Refresh and __Host-GeoSurvey.Organization.Refresh, Secure/HttpOnly/host-only/Path=/, SameSite=Strict. Rate limits and antiforgery registration/pipeline belong to auth API extension; root owns Program integration.

## Organization provisioning and activation

| Route | Input | Success data |
| --- | --- | --- |
| GET /platform/regions | Platform Bearer | `[{key,displayName}]`, local-demo only |
| GET /platform/organizations?pageNumber=1&pageSize=25 | Platform Bearer | `{items:[Organization],totalCount,pageNumber,pageSize}`; cap size at 100 |
| POST /platform/organizations | `{code,displayName,regionKey,administratorEmail}` + Platform Bearer | Organization; use 201 or 202 consistently |
| GET /platform/organizations/{publicId} | Platform Bearer | Organization |
| POST /platform/organizations/{publicId}/retry | Platform Bearer; no body | Organization |
| POST /platform/organizations/{publicId}/administrator-invitation | Platform Bearer; no body | `{token,expiresAtUtc,organizationCode,administratorEmail}`; UI builds /activate#TOKEN using its same origin |
| POST /activation/inspect | `{token}` + CSRF | `{organizationCode,organizationName,administratorEmail,expiresAtUtc}` |
| POST /activation/complete | `{token,password}` + CSRF | `{organizationCode,administratorEmail}`; recipient then signs in |
| GET /tenant/setup | Organization Bearer | `{organizationPublicId,organizationCode,organizationName,regionKey,state,schemaVersion,databaseIdentityVerified:true,administratorAccess:true}` only after actual resolution/marker/grant checks |

Organization shape: `{publicId,code,displayName,regionKey,state,administratorEmail,createdAtUtc,updatedAtUtc,failureCode:null|string}`. State values: Pending, Provisioning, AwaitingAdministrator, Ready, Failed, Suspended. An invitation may be issued/reissued only for AwaitingAdministrator; activation makes Ready after grant and health prerequisites. Error text must not leak database names/connection strings. Tenant setup booleans are measured enforcement outcomes, not placeholders.

Config: Tenancy:LocalDemo:RegionKey (local-demo), RegionDisplayName, RuntimeConnectionString, DatabasePrefix (GeoSurveyTenant_). Only the worker receives Tenancy:LocalDemo:ManagementConnectionString. The API's registration must not require or receive management credentials. Worker also receives ConnectionStrings:GlobalControlPlane and ConnectionStrings:ProvisioningJobs. Tenant context resolution changes InitialCatalog only from the trusted registry/profile and checks the tenant identity marker.

Tenancy.Application owns ITenantAdministratorAccess.CanAccessAsync(Guid accountPublicId, Guid organizationPublicId, CancellationToken). Auth uses it to verify the current global account's actual tenant grant; this method must resolve the registry internally and never trust a client SQL target. Keep contracts free of EF/ASP.NET types.

## Ownership and integration

- Authentication agent: Identity.Application, Identity.Infrastructure auth additions, ControlPlane.Infrastructure/Authentication, GeoSurvey.Api/Authentication extensions and auth package changes/new auth tests.
- Tenancy agent: Organizations.Application, Tenancy.Application/Infrastructure, ControlPlane.Infrastructure/Tenancy, provisioning worker, GeoSurvey.Api/Organizations extensions/new tenant tests; tenant migrations only.
- Frontend agent: existing Angular app and frontend/browser test files.
- Root: Program/composition integration, global context configurations/migrations/count-test updates, root solution/project integration, Docker/scripts/CI, cross-feature acceptance and submission docs/package.

Global model changes go to root as explicit configurations. Do not let two agents generate/edit the global snapshot simultaneously. No commit or push is authorized.
