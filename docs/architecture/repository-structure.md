# Repository structure and dependency boundaries

Status: Phase 0 accepted; the approved full Phase 1 local Docker delivery is complete. The [validation report](../submission/phase-1/validation-results.md) records backend, fresh Docker/browser and extracted-source evidence. Historical foundation descriptions below do not describe the current integration status.

## Current Phase 1 projects

`GeoSurvey.Api` is the HTTP/composition boundary. Identity, Organizations and Tenancy expose dependency-free Application contracts; Domain stays independent of EF/HTTP. `GeoSurvey.ControlPlane.Infrastructure` owns global persistence and cross-module composition, Identity Infrastructure owns authentication primitives, and Tenancy Infrastructure owns tenant contexts, marker/grant verification and provisioning. Each authenticated tenant request opens a fresh context using trusted registry/account data.

`GeoSurvey.Provisioning.Worker` hosts durable Hangfire jobs and the outbox dispatcher with separate privileged configuration. `GeoSurvey.LocalDemo` is operator setup tooling for external private configuration, migrations, restricted logins and masked bootstrap. Neither adds a public administrator-creation endpoint. The Angular app implements the approved Phase 1 screens; MAUI remains a separate skeleton and later-phase modules remain unimplemented.

See [the submission architecture](../submission/phase-1/architecture-overview.md), [shared API contracts](phase-1-integration-contracts.md), [ADR 0041](../decisions/0041-phase-1-local-submission-delivery.md), and the dedicated `docker-compose.phase1.yml`. The earlier foundation layout below is retained as historical context.

The source of truth is [the master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md), especially sections 3, 4, 5, 16, and 17. This document records the planned organization; it does not change business requirements or resolve open decisions.

## Purpose

GeoSurvey will use a monorepo: backend, web, mobile, tests, and deployment instructions live in one repository. The backend starts as one ASP.NET Core modular monolith. Modules separate business responsibilities while remaining part of the same application deployment.

Clean Architecture keeps business rules independent of HTTP, databases, and other infrastructure. In simple terms: business rules define what must happen; infrastructure supplies the technical mechanisms.

The [architecture decision index](../decisions/README.md) records the reasoning and consequences of the [modular monolith baseline](../decisions/0001-modular-monolith.md) and the [separate mobile solution](../decisions/0002-separate-mobile-solution.md).

## Planned layout

Create each item only in its approved implementation task. This is a target layout, not a list of existing files.

```text
GeoSurvey.sln
AGENTS.md
README.md
GeoSurvey_Master_Architecture_and_Codex_Spec.md
src/
  Backend/
    GeoSurvey.Api/
    BuildingBlocks/                 # Shared capabilities only when needed
    Modules/                        # Business modules only when needed
    Workers/                        # Separate worker host only when justified
  Frontend/
    geosurvey-web/
  Mobile/
    GeoSurvey.Mobile.sln            # Independent platform-specific builds
    GeoSurvey.Mobile/
tests/
  Unit/
  Integration/
  Architecture/
  EndToEnd/
deploy/
  docker/
  azure/
  pipelines/
docs/
  architecture/
  decisions/
  api/
  operations/
scripts/
docker-compose.yml                 # Add when local dependencies are needed
```

- `GeoSurvey.Api` hosts HTTP endpoints and composes dependencies through Dependency Injection.
- `Modules` will contain responsibilities such as Organizations, Identity, Projects, GIS, Assignments, and Surveys. They are deferred until their approved phases.
- `BuildingBlocks` is for justified shared capabilities, not a place to move module-specific business logic.
- The Angular application will use feature-based organization. A UI component library remains undecided.
- The MAUI application requires verification of the installed SDK, workloads, and target platform tooling before scaffolding/building.
- Test folders distinguish behavior tests, real infrastructure/API integration checks, dependency rules, and complete user workflows. Add test projects when they have meaningful checks to run.
- Deployment files, CI, Docker dependencies, and scripts will be introduced incrementally with exact run instructions.

## Module organization

Organizations Domain now includes the minimal `Organization` entity. `Create(OrganizationCode, string)` rejects null inputs, trims the display name, enforces 1-200 .NET string characters, generates PublicId with `Guid.NewGuid()`, and assigns `DateTimeOffset.UtcNow`. Its constructor/private setters prevent ordinary callers changing identifiers, code, name, or creation time; the integer Id remains zero before persistence. No interface, package, rename operation, provisioning state, API, or EF dependency is introduced. Name validation imposes no additional character restrictions or uniqueness. Unit tests verify creation, boundaries, null/empty names, independent IDs with duplicate names, code preservation, and a UTC timestamp bounded by creation calls. Later persistence mapping must review constructor/materialization behavior and enforce code/PublicId uniqueness in SQL Server.

`IdentityAccount` now derives from `IdentityUser<int>` in Identity Infrastructure. `CreateOrganizationAccount(int, string)` requires a positive organization ID; `CreatePlatformAccount(string)` creates Platform scope without an organization ID. Both validate through `AccountEmail`, generate `PublicId` with `Guid.NewGuid()`, derive `UserName` with `ToString("N")`, and normalize the username through Identity. `AccountScope` explicitly distinguishes Platform and Organization; enum numeric values are implementation details, not an approved API/storage encoding.

The account constructor is private; `PublicId`, `AccountScope`, and `OrganizationId` have private setters. Inherited Identity fields remain mutable for framework compatibility, so factories establish initial state rather than guaranteeing permanent invariants. Controlled operations and future database constraints must prevent scope changes or identifier/email inconsistencies. Integer `Id` stays at zero until persistence assigns it. No password, role, admin permission, MFA enrollment, database record, or token is created by these factories.

`Microsoft.Extensions.Identity.Stores` `10.0.9` supplies `IdentityUser<int>` and is required in addition to Identity Core. It does not add EF Core persistence. Unit tests cover both scopes, organization-ID guards, identifier derivation, email validation/normalization, and independent accounts sharing an email. These are object-creation tests, not evidence of database uniqueness or secure login. No API reference, DI registration, or migration is added.

Identity Infrastructure now contains `AccountEmail` in `src/Backend/Modules/Identity/GeoSurvey.Identity.Infrastructure`. This `net10.0` project references `Microsoft.Extensions.Identity.Core` version `10.0.9` for the approved `UpperInvariantLookupNormalizer`; Data Annotations validation comes from the framework. Domain remains free of Identity dependencies. The matching `tests/Unit/GeoSurvey.Identity.Infrastructure.Tests` project reuses existing xUnit/test SDK versions, and both projects are in the root solution. The API does not reference or register this component yet.

`AccountEmail.Create(string)` rejects null, trims with `.Trim()`, rejects empty or over-254-character values, rejects remaining whitespace/non-ASCII characters, checks `EmailAddressAttribute`, and normalizes the validated address with `UpperInvariantLookupNormalizer`. A private constructor and read-only `DisplayValue`/`NormalizedValue` keep the representations together. Invalid values raise `ArgumentException` (null raises `ArgumentNullException`) without echoing the supplied address. No interface or DI registration is needed for this deterministic creation operation. Syntax validation does not establish ownership, scope, uniqueness, or authorization.

The component is available for future organization/platform use cases; neither login flow exists yet. Its 23 tests cover null/invalid input, trimming (including surrounding Unicode whitespace before ASCII validation), address boundaries, Turkish-culture normalization, and preservation of dots/tags. No EF Core, accounts, endpoints, migrations, or database lookups were added by this task. Exact SQL mapping and scoped uniqueness remain future work.

The first implemented business module assembly is `src/Backend/Modules/Organizations/GeoSurvey.Organizations.Domain`, targeting `net10.0` with no package or project references. It currently contains only the immutable `OrganizationCode` value object. Its separate `tests/Unit/GeoSurvey.Organizations.Domain.Tests` project references Domain and reuses the existing xUnit/test SDK versions. Both projects are included in the root solution. No Application, Infrastructure, or module Presentation projects are created until needed; the API has no reference to Domain yet.

`OrganizationCode.Create()` trims surrounding whitespace using .NET `Trim()`, applies invariant lowercase normalization, and enforces the approved normalized length/character/hyphen rules. Null throws `ArgumentNullException`; other invalid input throws `ArgumentException` without including the supplied value. A private constructor and read-only `Value` prevent mutation; sealed record equality compares the normalized value. `ToString()` returns that value. Persistence must enforce platform-wide uniqueness later, and organization creation/update use cases must enforce administrator selection and code immutability after creation. This type does not authenticate users, choose a tenant database, or implement an API error contract.

Unit tests cover normalization, inclusive length boundaries, invalid characters and hyphens, null input, Turkish-culture casing independence, and value equality. Root build/test commands include these tests automatically. Architecture checks now discover backend `*.Domain.csproj` projects and reject every explicit package/project reference declaration, including conditional declarations. Standard framework SDK APIs remain available. The repository check requires the Organizations Domain project to be found; fixtures verify rejection of forbidden references and acceptance of dependency-free projects. This static XML rule does not evaluate imports or enforce assembly/namespace restrictions. A future shared Domain reference requires a reviewed exception.

A module can begin with folders. Separate assemblies are appropriate when they provide a concrete boundary or build/test benefit; do not generate every module as several empty projects.

```text
Modules/<ModuleName>/
  Domain/
  Application/
  Infrastructure/
  Presentation/
```

| Area | Responsibility | Dependency rule |
| --- | --- | --- |
| Domain | Entities, value objects, and business rules | No dependency on Application, Infrastructure, API, EF Core, or external services |
| Application | Use cases and required abstractions | Depends on Domain and abstractions |
| Infrastructure | Persistence and external service implementations | Implements Application abstractions; depends on Application/Domain |
| Presentation | HTTP endpoints and request/response mapping | Calls application use cases; keeps business rules out of endpoints |
| API host | Startup, HTTP hosting, and dependency registration | Composition root; connects implementations to abstractions |

An interface defines a capability a use case needs. An infrastructure class implements that capability. Dependency Injection connects the two at startup. Add an interface when this separation has a concrete purpose, rather than wrapping every class automatically.

Modules may interact through explicit public application contracts or events. They must not access another module's internal tables or DbContext, and must not form circular dependencies. Dependency checks will be added with the relevant code.

## Tenant security boundary

The planned database topology remains unchanged: a global control-plane database for identity and tenant registry, with an independent operational database for each organization. Module-based SQL schemas organize data within an organization's database; they do not replace database-per-tenant isolation.

Database selection must come from trusted server-side identity and registry data. Authorization must establish permitted organization membership and resource scope. A client-provided identifier alone never grants tenant access. No database, identity, or tenant resolution implementation is part of this documentation task.

Identity decision OPEN-001 was resolved on 2026-11-12: use independent organization accounts and credentials, including when the same email is reused across organizations. The global control-plane database still stores identity records and the tenant registry; global storage does not imply a shared login with organization memberships. Organization context must distinguish the account being authenticated, with exact login/account schema details still to be designed. See [decision record 0003](../decisions/0003-separate-organization-accounts.md).

Organization login approach approved on 2026-11-12: both web and mobile accept organization code + email + password. The server resolves the organization and validates its independent account; the code alone does not authenticate or authorize tenant access. See [decision record 0004](../decisions/0004-organization-code-login.md).

Organization-code rules approved on 2026-11-12: Platform Administrator selects a platform-wide unique code, fixed after creation. Normalize by trimming surrounding spaces and lowercasing; require 3-50 characters from `a-z`, `0-9`, and hyphens, without leading, trailing, or consecutive hyphens. See [decision record 0005](../decisions/0005-organization-code-rules.md). Database enforcement, reserved codes, and retired-code reuse remain unresolved/unimplemented as applicable.

Platform Administrator scope and login were approved on 2026-11-12: separate platform-scoped accounts in the global identity database use a dedicated email + password login without organization code. Server-side authorization restricts them to permitted platform operations; platform-admin status alone grants no access to tenant operational data. Platform-admin MFA is mandatory under [record 0008](../decisions/0008-platform-administrator-mfa.md): authenticator-app TOTP, enrollment before platform administration, and single-use recovery codes. Exact schema/API, enrollment implementation, and MFA reset/administrative recovery workflow remain open. Organization-user MFA remains undecided. First-administrator creation is defined in [record 0007](../decisions/0007-first-platform-administrator-bootstrap.md): a one-time authorized operator command with masked password input, application password hashing, no default credentials or public registration, and no account overwrite/password reset on rerun. The command creates an administrator only when none exists; exact implementation and concurrency controls remain to be designed. See [decision record 0006](../decisions/0006-platform-administrator-accounts.md).

Identity framework selection approved on 2026-11-12: ASP.NET Core Identity with EF Core belongs in Infrastructure, with no Identity/EF Core dependency in Domain. Account scope, organization lookup, uniqueness, and MFA/application flows require explicit configuration/customization. Physical schema, packages, endpoints, and migrations remain unimplemented and subject to approval. See [record 0010](../decisions/0010-aspnet-core-identity-ef-core.md) and the [identity-model proposal](identity-data-model-proposal.md).

## Phase 0 sequencing

Each step requires its own small task explanation and approval:

1. Record development instructions and repository structure (complete).
2. Propose and create the solution and minimal API host, with exact SDK configuration and run instructions (implemented: `GeoSurvey.sln`, `GeoSurvey.Api`, and `global.json`; see the root README).
3. Add health checks and structured logging with meaningful verification (complete: `/health/live`, Serilog compact JSON console logs, and a liveness integration test).
4. Add dependency checks as supported by the skeleton (implemented: static project reference checks prevent production-to-test and backend/mobile references, reject unknown targets, and detect cycles; checks for internal module layers remain deferred until those layers exist).
5. Verify tool compatibility and create the Angular skeleton (implemented: Angular 21 standalone app with strict TypeScript, routing configuration, and a root component test; frontend CI configuration implemented and hosted run passed).
6. Verify target tooling and create the MAUI skeleton (implemented: Android-only `GeoSurvey.Mobile` in the separate `src/Mobile/GeoSurvey.Mobile.sln`; Debug Android build verified, device startup pending because no device/emulator was attached).
7. Add relevant Docker support and a basic CI build/test pipeline (backend/frontend CI and API Docker configuration implemented; Compose validation, Linux image build, non-root container startup, and HTTP liveness verified; backend/frontend clean Git checkout verified on Windows; hosted backend/frontend CI passed for commit `e5072b4`).

The precise scope of each step will be proposed before implementation. Phase 0 finishes when a clean checkout can build and run locally with documented commands. It includes no authentication, tenant provisioning, GIS, or survey features.

Fresh source verification on 2026-11-12 passed: a temporary copy of Git-listed, non-ignored files restored and built the backend in Release mode with 8 passing tests; the copied API returned `200 Healthy`. Angular `npm ci`, production build, and its 1 component test also passed. Existing shared package caches and installed tooling were available. That check was not an actual Git checkout (the repository had no commits at the time); the later clean-checkout result is recorded below. MAUI, Docker, and formatting were not repeated in this task. See the root README for exact commands and unresolved dependency findings.

## Clean checkout result

Clean Git checkout verification on 2026-11-12 passed for commit `12ef425`: an independent temporary local clone restored and built the backend with zero warnings/errors, passed 8 backend tests, and served `200 Healthy`. Angular clean installation, production build, and its 1 component test also passed. This verifies committed source on the existing Windows environment, with shared package caches available; it does not verify a clean machine or GitHub hosted CI. MAUI, Docker, and formatting were not repeated. See the root README for commands and remaining limitations.

## Phase 0 acceptance status

On 2026-11-12, the master specification's Phase 0 and section 17.3 deliverables were reviewed against the existing implementation and verification evidence. All required skeleton deliverables are present. [Hosted CI run 43205939746](https://github.com/mbilalyousaf1/Geo-Survey_managment_system/actions/runs/43205939746) completed successfully for commit `e5072b4`, with both backend and frontend jobs passing. The clean-checkout result above and earlier Docker/Android build evidence remain applicable within their stated limits.

Recommendation: ready for user acceptance with documented limitations, rather than an automatic declaration of completion. The 16 moderate frontend development-dependency findings are unresolved; Android device startup and mobile CI remain unverified/unconfigured. Clean checkout used existing tooling/shared caches. Branch protection and production deployment are not established. See the root README's Phase 0 acceptance review for evidence and scope. No Phase 1 implementation is authorized by this review.

## Module persistence implementation

Operator console, 2026-11-17: `src/Backend/GeoSurvey.Bootstrap` is a separate composition root referencing ControlPlane Infrastructure; `tests/Unit/GeoSurvey.Bootstrap.Tests` verifies terminal/configuration/result behavior. The API has no console reference. See [operator setup and verification](../development/bootstrap-console.md).

Bootstrap password base components, 2026-11-16: Identity Infrastructure now contains `BootstrapPassword` and `IdentityPasswordHashingOptions`, with no new dependencies, API registration, or database changes. [Explanation and unit-test results](../development/bootstrap-password-components.md) document NFC/code-point rules and real framework hashing checks. This is not complete blocklist validation or a bootstrap command.

API composition, 2026-11-16: `GeoSurvey.Api` now references ControlPlane Infrastructure and registers the global context as scoped with startup-validated connection options. No Identity login services or automatic migration are registered. Docker build inputs include the referenced production projects and Compose forwards environment configuration. See [API configuration and verification](../development/api-control-plane-configuration.md). Earlier pending-registration statements below describe the preceding slices.

Migration execution verification, 2026-11-16: `MigrationDatabase` and `MigrationApplicationTests` in the ControlPlane integration-test project apply the migration using `MigrateAsync` to a previously absent disposable database, then verify reapplication, history, actual schema/FKs, and scoped uniqueness. [Results and limits](../development/migration-application-tests.md) distinguish this from the existing `EnsureCreated` fixtures and from production deployment.

Design-time tooling, 2026-11-16: `.config/dotnet-tools.json` pins EF 10.0.9; ControlPlane Infrastructure contains `GlobalControlPlaneDbContextFactory` and the generated initial migration/snapshot under `Migrations`. [Generation and SQL review](../development/initial-global-migration.md) records the explicit environment configuration and review artifact. Migration application is now verified by the dedicated disposable fixture noted above; other model-only tests still use `EnsureCreated`.

Global composition, 2026-11-16: `src/Backend/GeoSurvey.ControlPlane.Infrastructure` references Identity/Organizations Infrastructure and owns `GlobalControlPlaneDbContext`, SQL Server options/history configuration, and the account-to-organization FK. Module references do not point back to ControlPlane. `tests/Integration/GeoSurvey.ControlPlane.Infrastructure.IntegrationTests` verifies the production context against SQL Server. See [context and tests](../development/control-plane-persistence-tests.md). API registration and migrations remain pending.

Identity persistence implementation, 2026-11-14: `GeoSurvey.Identity.Infrastructure/Persistence` now owns explicit account/claim/login/token mappings and shared schema options. `tests/Integration/GeoSurvey.Identity.Infrastructure.IntegrationTests` verifies them with a role-free test-only Identity context and SQL Server. No Organizations reference or Organization FK is introduced in Identity. Production global composition remains separate. See [Identity mapping and tests](../development/identity-persistence-tests.md).

Phase 1 Organization mapping now lives in `src/Backend/Modules/Organizations/GeoSurvey.Organizations.Infrastructure`, referencing Domain and EF Core Relational. SQL Server tests live in `tests/Integration/GeoSurvey.Organizations.Infrastructure.Tests` with a test-only context. Production global context and migrations remain unimplemented. See [mapping and test instructions](../development/organization-persistence-tests.md).

## Initial environment inspection

Observed on 2026-11-11:

- The repository initially contained only the untracked master specification and Git metadata; no solution, application, tests, or `AGENTS.md` existed.
- Git reported no commits on `main` and an unavailable tracked upstream reference. Remote availability was not checked over the network.
- Installed .NET SDKs: `9.0.312` and `10.0.301`; no `global.json` existed.
- .NET reported Android, iOS, Mac Catalyst, and Windows MAUI workloads. Platform builds and external tooling were not verified.
- Node.js `24.14.1`, npm `11.11.0`, and Git `2.53.0.windows.2` were available.
- Docker CLI `29.6.2` was available, but `docker info` could not reach the Docker Desktop Linux engine.

These are local observations, not pinned dependency versions or compatibility guarantees. Select and verify framework/package versions in the affected scaffolding task. Resolve the specification's open business decisions only before their affected features; they do not block this documentation task.

MFA challenge lifecycle, 2026-11-17: Identity Infrastructure owns the MFA model/mapping and persistence interface; ControlPlane Infrastructure implements that interface using the composed global context. No API endpoint or DI registration is enabled. See [ADR 0030](../decisions/0030-restricted-mfa-challenges.md).

TOTP protection: Identity Infrastructure contains ITotpSecretProtector and its Data Protection implementation. Host-owned key configuration is not yet wired. See [component notes](../development/totp-secret-protection.md).

API key host: Configuration/DataProtectionRegistration.cs owns certificate loading, dedicated provider lifecycle, settings validation, and startup key checks. See [host key runbook](../development/data-protection-host.md).

Pending MFA enrollment: Identity Infrastructure owns PendingTotpEnrollment, its configuration/contract/service; ControlPlane Infrastructure implements scoped transactional persistence. See [implementation notes](../development/pending-totp-enrollment.md).

TOTP calculation: Identity Infrastructure/Mfa/TotpAuthenticator.cs provides secret generation and stateless matched-step verification; published-vector tests are in Identity unit tests. See [component notes](../development/totp-primitive.md).

MFA controls: Identity Infrastructure owns MfaVerificationState, policy/contract/service; ControlPlane Infrastructure supplies SQL attempt/replay transactions and shared active-block eligibility. See [control notes](../development/mfa-attempt-replay-controls.md).

Enrollment confirmation: Identity Infrastructure owns the confirmation contract/result, confirmed-credential/recovery models, and issuance helper. ControlPlane Infrastructure owns MfaEnrollmentConfirmationService and the transaction across controls, credentials, stamps, and pending-state transfer. See [workflow notes](../development/mfa-enrollment-confirmation.md).

Platform password entry: Identity Infrastructure/SignIn owns normalization, policy, contract/result; ControlPlane Infrastructure/SignIn owns the private-context transaction and scoped credential verification. The existing challenge issuer provides a non-throwing TryIssueAsync path. See [workflow notes](../development/platform-password-entry.md).

Confirmed-factor verifier: Identity Infrastructure/SignIn owns IPlatformMfaVerifier and the provisional public-subject result; ControlPlane Infrastructure/SignIn owns token routing, current-factor verification, and participation in the caller's completion transaction. See [workflow notes](../development/platform-mfa-verification.md).

Session storage: Identity Infrastructure/Sessions owns TokenFamily, RefreshToken, and their mappings; only the global control-plane model composes these records. ControlPlane Infrastructure owns the seventh migration. SQL constraints/upgrade tests live in its integration project; factory validation lives in Identity unit tests. See [storage notes](../development/session-refresh-token-storage.md). No issuer, authentication endpoint, or lifecycle service is enabled by these models.

Session start: Identity Infrastructure/Sessions owns PlatformSessionPolicy; SignIn owns IPlatformSessionStarter and its non-record result/ticket. ControlPlane Infrastructure/SignIn owns PlatformSessionStarter and its private completion transaction. Contract tests live in Identity unit tests; real workflow/commit/fault/concurrency tests live in ControlPlane SQL integration tests. See [implementation notes](../development/platform-session-start.md). This adds no migration, JWT issuer, or host/public endpoint registration.
