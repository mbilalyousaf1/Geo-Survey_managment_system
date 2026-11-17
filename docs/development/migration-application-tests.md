# Initial migration application tests

Current update, 2026-11-17: [bootstrap-completion storage](bootstrap-completion-storage.md) adds a second migration. Current fresh-install and history tests expect both migrations and seven physical tables including history; a separate fixture tests upgrading from the unchanged initial migration. Original results/counts below describe the earlier single-migration slice.

Implementation and verification date: 2026-11-16.

Six new SQL Server tests execute the generated initial migration against a fresh disposable database. This closes the gap between generating/reviewing a migration and proving its `Up` operations work. Production application code, migration files, packages, and the Docker runner are unchanged.

## Fixture lifecycle

`MigrationDatabase` is a dedicated xUnit asynchronous fixture in `GeoSurvey.ControlPlane.Infrastructure.IntegrationTests`. It reads the existing runner's `GEOSURVEY_TEST_SQLSERVER` configuration, selects a new `GeoSurvey_MigrationTests_<random-guid>` database name, and configures the actual `GlobalControlPlaneDbContext` through the approved helper and Identity options.

After SQL Server readiness, a parameterized `DB_ID` query against master confirms the chosen database does not exist. If it unexpectedly exists, initialization refuses to modify or delete it. After this check, `Database.MigrateAsync()` creates the database and applies its migrations. No `EnsureCreated` call occurs in this fixture.

The fixture marks its generated target for cleanup before migration execution, so a partially created database can also be removed after failure. Disposal calls `EnsureDeletedAsync` only against that newly selected target, then disposes its options service provider. The existing runner removes its uniquely named container in its own cleanup. No existing configured application database is migrated.

Missing connection configuration fails explicitly. No connection values, credentials, or sensitive test payloads are logged. The tests use synthetic account data; neither migration application nor a successful insert grants application authorization.

## What is verified

| Scenario | Observed behavior |
| --- | --- |
| Initial migration from an absent database | Applied migration matches the single source migration; no pending migrations remain |
| Database objects | Five approved application/framework tables plus `controlplane.__EFMigrationsHistory`; no extra tables |
| History | One row with the expected migration ID and EF product version 10.0.9 |
| Delete actions | All four actual SQL Server FKs have NO ACTION delete behavior |
| Repeat `MigrateAsync` | Existing Organization/account identifiers, association, email, creation values, concurrency stamp, and the single history row are preserved; no pending migrations remain |
| Missing Organization reference | SQL Server rejects the account insert |
| Organization deletion with an account | SQL Server rejects direct deletion and preserves both records |
| Scoped email uniqueness | Same email is accepted across two real organizations and Platform scope; normalized duplicates are rejected within an organization and within Platform scope |

`MigrationApplicationTests` contains these six cases, including two scoped-duplicate theory cases. Existing model-only fixtures still use `EnsureCreated` for their separate tests. The migration fixture does not convert or reuse those databases.

## Commands and results

From repository root:

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

PowerShell 7/CI uses `./scripts/Test-Backend.ps1` after restore/build. See [runner prerequisites and cleanup](organization-persistence-tests.md), [generated migration review](initial-global-migration.md), and [context composition](control-plane-persistence-tests.md).

Local results: build passed with zero warnings/errors; formatting passed; all 154 backend tests passed with zero failures/skips. Counts: ControlPlane 25 (including six migration-application tests), Identity integration 35, Organization integration 7, Identity unit 35, Organization unit 38, Architecture 13, API 1. Restore was not repeated because no package or project dependencies changed.

## Limits and next work

The migration was applied and reapplied only inside the disposable test database. The saved review SQL script was not executed directly; SQL-script deployment, upgrades from populated historical schemas, rollback, backups, production permissions, and production release procedures remain unverified. No API registration, real environment provisioning, tenant routing, authentication, or authorization flow was added. Hosted CI, frontend, and mobile checks were not run. No commit or push was requested or performed.

Recommended next small task: plan API context registration and secure local control-plane configuration, without automatic startup migration or provisioning.
