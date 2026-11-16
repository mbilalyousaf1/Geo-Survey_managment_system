# Global control-plane context and tests

Implementation date: 2026-11-16.

Current integration: [API scoped registration and configuration](api-control-plane-configuration.md) is now implemented, with database-independent startup/liveness and no automatic migration. Earlier no-registration statements below describe the original composition task boundary.

Subsequent slice: [initial migration generation and review](initial-global-migration.md) adds design-time tooling and a generated migration/snapshot, without applying it. The historical results below describe context composition; the existing database fixtures still use `EnsureCreated`, not migration execution.

Latest verification: a separate [migration fixture and six tests](migration-application-tests.md) now apply/reapply the initial migration in a previously absent disposable database. Original composition fixtures still use `EnsureCreated`; production/API configuration remains unchanged.

`GeoSurvey.ControlPlane.Infrastructure` composes the existing Identity and Organizations mappings into `GlobalControlPlaneDbContext`. This is a global identity/organization context, not a tenant operational context. It is not registered in the API and has no provisioning workflow or production database connection configured. Migration artifacts now exist as noted above, but are unapplied.

## What the classes do

`GlobalControlPlaneDbContext` extends the accepted role-free `IdentityUserContext<IdentityAccount, int>`. Its model configuration calls Identity's base configuration, applies the module-owned account/supporting mappings and Organization mapping explicitly, then adds the cross-module relationship:

```text
identity.Accounts.OrganizationId -> organization.Organizations.Id
```

The relationship is optional so Platform accounts can have no organization. Existing account CHECK rules require Organization accounts to supply a positive OrganizationId; the composed FK now additionally requires that organization to exist. `NO ACTION` blocks organization deletion while dependent accounts exist. Neither module gains a navigation to the other's entity or a reference back to ControlPlane.

This is database referential integrity, not authorization. Runtime use cases must still resolve trusted organization scope and enforce permissions. The unrestricted context is not exposed to Application services. A blocked DELETE test does not approve any organization deletion or retention workflow.

`GlobalControlPlaneOptions.UseGeoSurveyControlPlaneSqlServer` accepts trusted server-side connection data and an application service provider containing Identity options configured through `IdentityPersistenceOptions.Configure`. It supplies the SQL Server provider, the application provider used by Identity's base model, and migration history `controlplane.__EFMigrationsHistory`. Calling it does not open a connection or apply migrations. It does not log connection data.

Both the configuration helper and the context constructor reject Identity options other than schema Version 2 and key length 128. Constructor validation runs for every context instance, even when EF reuses a cached model. Do not mutate Identity options after configuring the context. Future runtime and design-time setup must use the same configuration path and settings.

No `OnConfiguring` fallback, connection default, hardcoded credential, generic repository, or design-time factory is added. API registration, restricted application persistence contracts, and operator connection handling are later tasks.

## Dependencies and boundaries

- ControlPlane Infrastructure references the two module Infrastructure projects; neither module references ControlPlane.
- Production context needs `Microsoft.AspNetCore.Identity.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9. Its SqlClient dependency retains the existing patched `System.Security.Cryptography.Pkcs` 9.0.21 override; unlike earlier module tests this dependency now also belongs to production context infrastructure.
- Domain projects and existing module mappings are unchanged.
- The new integration-test project reuses the existing test SDK/xUnit versions and references ControlPlane Infrastructure. The existing Docker test runner discovers it through the solution; no workflow/script changes are needed.

## Tests and execution

The fixture configures the actual production context through its helper and uses a unique database inside the existing isolated SQL Server test container. It waits for readiness, uses `EnsureCreated` only for test setup, and deletes only its own generated database. Credentials come from the existing runner. Missing test connection configuration fails explicitly.

Fourteen tests cover:

- Platform and real organization accounts persist and reload with correct associations.
- Inserts and updates referencing missing organizations fail under SQL Server; failed updates preserve the original association.
- Direct SQL deletion of an organization with accounts is rejected and both records survive.
- Email reuse across two real organizations and Platform scope is allowed; concurrent same-scope duplicates yield exactly one success.
- The created database has precisely the five approved tables, with no global roles or passkeys. The composed FK is optional, uses NO ACTION, and introduces no cross-module navigation.
- The configured history repository generates SQL for the approved history schema/table. No migration or history table is applied by this test.
- Unapproved Identity schema/key settings are rejected, including callers bypassing the configuration helper.

Run from repository root:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

PowerShell 7/CI can invoke `./scripts/Test-Backend.ps1` after restore/build. See [Organization test-runner instructions](organization-persistence-tests.md) for container lifecycle and prerequisites, and [Identity mapping tests](identity-persistence-tests.md) for module-level coverage.

## Local verification — 2026-11-16

| Executed check | Observed result |
| --- | --- |
| `dotnet restore GeoSurvey.sln` | Passed; no warnings reported |
| `dotnet build GeoSurvey.sln --configuration Release --no-restore` | Passed; zero warnings/errors |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1` | 143 passed, zero failures/skips: ControlPlane 14, Identity integration 35, Organization integration 7, Identity unit 35, Organization unit 38, Architecture 13, API 1 |
| `dotnet format GeoSurvey.sln --verify-no-changes --no-restore` | Passed |
| `git diff --check`, additional new-file whitespace scan, and local documentation links | Passed |
| `docker ps --filter name=geosurvey-tests- --format '{{.Names}}'` | No temporary test containers remained |

Build, tests, and formatting were repeated after moving Identity-option validation to the constructor so cached models cannot bypass the per-instance check. Both full test runs passed; the table above describes the final version.

Hosted CI, frontend, and mobile checks were not run for this backend-only slice. No commit or push was performed.

## Remaining work

No production migration, migration snapshot, database deployment, API composition-root registration, tenant routing, provisioning, authentication, or authorization workflow is implemented by this slice. The module-only Identity test context still deliberately excludes Organizations; the new global context tests are the evidence for cross-module FK integrity. Migration generation/application and startup/design-time setup need separate approved tasks.
