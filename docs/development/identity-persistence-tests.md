# Identity persistence mapping and tests

Subsequent implementation, 2026-11-16: the [global control-plane context](control-plane-persistence-tests.md) now composes these mappings with Organizations and owns the cross-module FK. The module-only context and the original slice boundaries/results below remain unchanged. Production migrations and API registration remain pending.

The approved Identity mapping slice implements module-owned account and supporting-table configurations. A test-only `IdentityUserContext<IdentityAccount, int>` applies Identity's base configuration, followed by `ApplyGeoSurveyIdentityMappings`.

There is no production global context, Organization FK, migration stream, API registration, login flow, or credential/MFA workflow yet. The test model deliberately permits positive OrganizationIds without checking organization existence. It must not be used as a complete global persistence model.

## Implementation and learning notes

`IdentityAccountConfiguration` describes `identity.Accounts`: generated integer Id, application-generated unique PublicId, integer scope, required email/username lengths, binary normalized-column comparison, and the scope/OrganizationId CHECK constraint.

The two email indexes enforce different rules: `(OrganizationId, NormalizedEmail)` is unique only for Organization scope; `NormalizedEmail` is unique only for Platform scope. They allow the same email in multiple organizations and the platform while rejecting scoped duplicates under concurrent inserts. Existing Identity username/email indexes are reconfigured rather than duplicated. There is no unfiltered globally unique email index.

`AccountClaimConfiguration`, `AccountLoginConfiguration`, and `AccountTokenConfiguration` preserve framework keys and configure the accepted `identity.AccountClaims`, `identity.AccountLogins`, and `identity.AccountTokens` tables. Required account FKs use `NO ACTION`, blocking account deletion while dependent rows exist. This is not a retention or deletion workflow. Login and token composite key strings have explicit 128-character limits.

Each configuration implements EF's `IEntityTypeConfiguration<T>` contract so a context can explicitly apply module-owned rules. `IdentityModelBuilderExtensions` supplies one explicit registration method; it does not scan arbitrary assemblies or expose a global context. Identity Infrastructure has no Organizations reference. The future ControlPlane context will own cross-module FK composition.

`IdentityPersistenceOptions.Configure(IdentityOptions)` sets schema Version 2 and key length 128. Future runtime and design-time setup must use these same settings before constructing the model. The test fixture uses configured options through its application service provider, preserving Version 2 base behavior and excluding passkeys. Roles and user-role tables are absent. Configurations must be applied after Identity base model creation.

`ConcurrencyStamp` remains a concurrency token. The test uses two fresh contexts and Identity's `UserOnlyStore.UpdateAsync`; the first update changes the stamp and succeeds, and the stale second update returns `ConcurrencyFailure` without overwriting the first.

## Security and scope limits

- Database indexes enforce uniqueness, not authorization. Platform scope alone grants no administration privileges.
- Inherited Identity email properties remain mutable. Every real account write/lookup must use the approved `AccountEmail` normalization; mapping does not validate email syntax or ownership.
- SQL Server BIN2 comparison does not uppercase values. Tests demonstrate that noncanonical casing can bypass intended application-level case insensitivity when raw writes bypass normalization. SQL Server also treats trailing padding as equal for uniqueness. Direct-writer canonical-value guards remain a separate design.
- The test's scoped lookup is not a production resolver. Identity's generic email lookup must not be exposed as an organization-aware login; future services must resolve trusted scope/organization and avoid fallback across scopes.
- Standard login keys do not implement organization-scoped external login. Token tables do not implement JWT/refresh-token families, MFA secret protection, or encryption. Test values are synthetic nonsensitive mapping data.
- No Organization FK existence/deletion test is claimed. That FK belongs to the separate global composition task.

## Dependencies

Production Identity Infrastructure adds `Microsoft.EntityFrameworkCore.Relational` 10.0.9 to its existing Identity Core/Stores 10.0.9 references. This supplies mapping APIs without bringing Identity or EF into Domain.

The integration-test project adds `Microsoft.AspNetCore.Identity.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9, reuses existing xUnit/test SDK versions, and pins the existing SqlClient transitive `System.Security.Cryptography.Pkcs` dependency to patched 9.0.21, matching the Organization test project. No new test framework or container library is added.

## Run and verification

Use the existing [isolated SQL Server runner](organization-persistence-tests.md). From repository root:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

On PowerShell 7, including CI, invoke `./scripts/Test-Backend.ps1` after restore/build. The unchanged runner discovers the new project through the solution. It supplies generated credentials and cleans up its temporary container. Each Identity fixture creates/deletes its own randomly named database; no existing organization or production database is used. Missing connection configuration fails explicitly. `EnsureCreated` remains test-only and is not a migration strategy.

Local verification completed 2026-11-14:

| Check | Result |
| --- | --- |
| Restore | Passed; no warnings reported |
| Release build | Passed; zero warnings/errors |
| Formatting verification | Passed |
| `git diff --check`, new-file whitespace review, and documentation link checks | Passed |
| Isolated full backend test runner | 129 passed, zero failures/skips: Identity integration 35, Organization integration 7, Identity unit 35, Organization unit 38, Architecture 13, API 1 |

The 35 new tests cover fresh reads, scoped email reuse/lookups, concurrent scoped duplicates, invalid scope combinations, identifier uniqueness, required field/length boundaries, punctuation and raw comparison behavior, real store concurrency handling, supporting FK/deletion/key constraints, supporting store reads, and the reviewed model shape.

Hosted CI, frontend, and mobile were not executed for this slice. No production migration or database deployment was performed. No commit or push was requested or performed.
