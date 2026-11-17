# Permanent bootstrap-completion storage

Implementation and verification date: 2026-11-17.

This slice adds the accepted durable marker's storage model, EF mapping, second migration, and tests. It does not yet create an administrator, check the marker during bootstrap, or implement the console/transactional creation service.

## Model and mapping

ControlPlane Infrastructure owns `Bootstrap/BootstrapCompletion` and `BootstrapCompletionConfiguration`. The model's `Create()` sets Id 1 and application UTC time. A private constructor accepts persisted values without generating a replacement timestamp during EF materialization. There are no update/reset/delete methods.

`controlplane.BootstrapCompletion` has required integer primary key Id, a CHECK constraint `Id = 1`, and required `CompletedAtUtc datetimeoffset(7)`. Both properties are ValueGeneratedNever; there is no database timestamp default, identity generation, seeded marker, or account FK. The PK plus CHECK allow at most one row, while absence means no completion has been recorded. UTC is assigned by the application factory, not enforced by an additional unapproved SQL offset CHECK.

`GlobalControlPlaneDbContext` explicitly applies the mapping and exposes the DbSet for the future narrowly scoped bootstrap persistence implementation. The global model now contains six mapped tables, seven including migration history. Identity-only module contexts remain unchanged. API/design-time/model-count tests were updated accordingly.

## Migration and review

New migration: `20261117073638_AddBootstrapCompletion`. The initial migration and its designer were preserved. The updated snapshot describes the six-table model. [Marker-only review SQL](sql/add-bootstrap-completion.sql) covers the initial-to-second migration range and creates only the marker table, singleton CHECK/PK, and second history row. It contains no account creation, seeding, relationship, or alteration to existing business tables.

The generated Down method drops the marker table and would erase its operational history. No rollback was executed or approved as a recovery/reset workflow. Schema owners can still delete/drop data; the marker's permanence also depends on the separately planned operator/runtime privileges and creation guard. This slice does not claim the database record is unerasable by privileged administrators.

## Tests

- Fresh disposable migration databases apply both migrations and verify two history entries, six mapped tables plus history, and existing NO ACTION FKs.
- A separate `InitialMigrationDatabase` fixture applies only the original migration through the same guarded disposable-database setup. The upgrade test confirms the marker table is initially absent, inserts existing organization/account data, upgrades, and checks that data survives without a seeded marker.
- The upgrade test records completion, deletes its synthetic account directly in the disposable database, and verifies completion survives deletion and repeated migration. This verifies storage independence, not approval of a production administrator deletion workflow or a bootstrap refusal service that does not exist yet.
- Additional marker tests verify no seed, fixed key, timestamp precision/UTC materialization, duplicate-key rejection, invalid keys -1/0/2 rejected by SQL Server, and absence of account relationships/default timestamp generation.

There are six new tests. Existing migration-history/reapplication tests now expect two entries; the original migration fixtures still avoid EnsureCreated. Existing model-only fixtures retain their separate EnsureCreated behavior. No new package or project was needed.

## Commands and results

The design-time commands supplied the same explicit credential-free offline connection used by previous generation tasks and restored the previous environment afterward:

```powershell
dotnet ef migrations add AddBootstrapCompletion --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext --output-dir Migrations
dotnet ef migrations script InitialGlobalControlPlane AddBootstrapCompletion --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext --output docs/development/sql/add-bootstrap-completion.sql
dotnet ef migrations has-pending-model-changes --project src/Backend/GeoSurvey.ControlPlane.Infrastructure --startup-project src/Backend/GeoSurvey.ControlPlane.Infrastructure --context GlobalControlPlaneDbContext --no-build
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

The first scaffold/build attempt failed with CS1529 due to a misplaced using directive; it was corrected before successful generation. New generated files were normalized to repository UTF-8 encoding without changing migration semantics. Final generation and snapshot checks passed; no pending model changes. Release build passed with zero warnings/errors. Formatting passed. Full backend run: 214 passed, zero failures/skips (ControlPlane 31, Identity unit 82, Organization unit 38, Architecture 13, API 8, Identity integration 35, Organization integration 7).

Only fresh disposable test databases were migrated. No existing application database was updated, no administrator/real credential was created, and the saved SQL script was reviewed rather than executed directly. Runtime/operator grants, atomic account/claim/marker writes, concurrency-safe guard, and console remain pending. Restore was not repeated because no packages changed. Hosted CI, Docker image rebuild, frontend, and mobile were not run. Nothing was committed or pushed.

## References

- [Permanent completion decision](../decisions/0027-permanent-bootstrap-completion.md)
- [Migration execution fixture](migration-application-tests.md)
- [Console/operator decision](../decisions/0026-bootstrap-console-and-operator-access.md)
- [Phase 1 remaining work](phase-1-completion-plan.md)
