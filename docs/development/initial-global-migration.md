# Initial global migration: generation and review

Implementation date: 2026-11-16.

The repository now has pinned local EF tooling, a design-time context factory, migration `20261116114419_InitialGlobalControlPlane`, its designer/model snapshot, and a [review SQL artifact](sql/initial-global-control-plane.sql). This original slice generated and reviewed artifacts only. A subsequent approved slice now [tests migration application](migration-application-tests.md) in a fresh disposable database; the saved SQL script is still unexecuted. Generation-stage results below remain historical evidence for that narrower task.

## How it works

`GlobalControlPlaneDbContextFactory` implements EF's `IDesignTimeDbContextFactory<GlobalControlPlaneDbContext>`. EF tooling can build the approved model without starting the API. The factory requires `GEOSURVEY_DESIGNTIME_SQLSERVER`; missing/whitespace configuration fails clearly. It accepts no additional arguments and does not echo their contents or connection data.

The factory reuses `IdentityPersistenceOptions.Configure` and `UseGeoSurveyControlPlaneSqlServer`, so provider, Identity schema Version 2, key length 128, and migration-history location agree with the composed context. A lazy process-lifetime service provider holds fixed nonsensitive Identity options; connection strings are read per factory call and are not stored in that shared provider. Creating the context does not open a connection or execute SQL.

`.config/dotnet-tools.json` pins `dotnet-ef` 10.0.9. ControlPlane Infrastructure references `Microsoft.EntityFrameworkCore.Design` 10.0.9 with `PrivateAssets=all` so tooling dependencies do not flow transitively to consumers. No API startup changes or new runtime registration are added.

The migration's `Up` method describes schema creation. `Down` describes dropping the five tables in dependency order and would delete their data if executed. Generating this method does not authorize rollback or change deletion/retention policy; no rollback was run. The model snapshot records the expected schema so EF can calculate later changes.

## Generate without a database connection

From repository root:

```powershell
dotnet tool restore
dotnet restore GeoSurvey.sln
```

For model-only generation/checks, the following explicitly supplies a credential-free, unreachable endpoint for this process. It is not a factory fallback or application default. Keep real connection data out of source and shell history; database-targeting commands need separately reviewed operator configuration and authorization.

The initial migration already exists. The scaffold command below records how it was created; do not rerun it under the same name on the current checkout.

```powershell
$previousDesignConnection = $env:GEOSURVEY_DESIGNTIME_SQLSERVER
try {
    $env:GEOSURVEY_DESIGNTIME_SQLSERVER = 'Server=127.0.0.1,1;Database=GeoSurvey_DesignTimeModel;Integrated Security=True;Encrypt=True;Connect Timeout=1'

    # Historical scaffold command; already executed for the repository artifact:
    # dotnet ef migrations add InitialGlobalControlPlane --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext --output-dir Migrations

    dotnet ef migrations script 0 InitialGlobalControlPlane --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext --output docs/development/sql/initial-global-control-plane.sql
    if ($LASTEXITCODE -ne 0) { throw 'Migration SQL generation failed.' }

    dotnet ef migrations has-pending-model-changes --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext
    if ($LASTEXITCODE -ne 0) { throw 'Migration model verification failed.' }
}
finally {
    $env:GEOSURVEY_DESIGNTIME_SQLSERVER = $previousDesignConnection
}
```

Commands use the context project as both target and startup project, bypassing the API. Generation uses the SQL Server model but does not connect. EF tooling emitted a UTF-8 BOM in generated output during this task; normalize generated artifacts to UTF-8 without BOM to match `.editorconfig`, then run formatting checks. No generated schema semantics were manually changed.

## SQL review findings

- The script creates history schema/table `controlplane.__EFMigrationsHistory`, then creates `identity` and `organization` schemas as needed.
- Five application/framework tables appear: Organizations, Accounts, AccountClaims, AccountLogins, and AccountTokens. History is a sixth infrastructure table when this script is eventually applied. There are no role/passkey tables or seeded accounts/credentials.
- Organizations precedes Accounts; Accounts precedes dependent supporting tables. Integer identities, GUIDs, required lengths, Code/normalized-field BIN2 collations, and `datetimeoffset(7)` creation time match the approved mappings.
- The account scope/OrganizationId CHECK and filtered unique organization/platform email indexes are present. There is no globally unique unfiltered email index.
- All four FKs omit cascading actions; SQL Server's default is NO ACTION. PublicId/username/code uniqueness and supporting composite keys are represented.
- A transaction wraps the initial schema operations and migration-history insertion. The artifact contains no connection string or application data.
- This is a non-idempotent `0 -> InitialGlobalControlPlane` script. The history-table existence guard does not make the table-creation statements repeatable. It is not ready to apply to an existing `EnsureCreated` database or an arbitrary environment.

No application startup migration call was introduced. Migration application and history/reapplication are now verified by the separate disposable test fixture linked above; production deployment strategy remains open.

## Verification scope

Five new tests cover missing/whitespace configuration, rejected arguments without input echo, factory model/provider/history settings, and snapshot/SQL consistency. They use the explicit offline endpoint and do not connect. The existing history test now expects the initial migration rather than an empty migration list.

At the generation-only stage, existing Docker fixtures used `EnsureCreated` and did not validate migration execution. The subsequent migration-application fixture now verifies execution separately without `EnsureCreated`; the older fixtures retain their model-focused scope.

## Local verification — 2026-11-16

| Executed check | Result |
| --- | --- |
| `dotnet tool restore` | Restored local `dotnet-ef` 10.0.9 successfully |
| `dotnet restore GeoSurvey.sln` | Passed; no warnings reported |
| `dotnet ef migrations add InitialGlobalControlPlane` with project/startup/context/output flags recorded above | Generated migration, designer, and model snapshot successfully |
| `dotnet ef migrations script 0 InitialGlobalControlPlane` with recorded flags/output path | Generated review SQL successfully; no execution |
| `dotnet ef migrations has-pending-model-changes` with recorded project/startup/context flags and `--no-build` | No model changes since the generated migration |
| `dotnet build GeoSurvey.sln --configuration Release --no-restore` | Passed; zero warnings/errors |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1` | 148 passed, zero failures/skips: ControlPlane 19, Identity integration 35, Organization integration 7, Identity unit 35, Organization unit 38, Architecture 13, API 1 |
| `dotnet format GeoSurvey.sln --verify-no-changes --no-restore` | Initially failed with CHARSET on generated migration; passed after normalizing generated UTF-8 encoding without changing semantics |

The temporary test container was removed. A restricted Docker readiness check initially failed with pipe permission denied; the authorized runner succeeded outside that restriction. No migration application, rollback, API runtime registration, hosted CI, frontend, or mobile verification occurred. Nothing was committed or pushed.
