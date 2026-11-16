# Organization persistence mapping and tests

Subsequent implementation, 2026-11-16: the [global control-plane context](control-plane-persistence-tests.md) now composes Organization and Identity mappings and tests their FK. The standalone Organization context and original slice results below remain applicable to their narrower scope. Production migrations and API registration remain pending.

The approved Organization mapping slice adds `GeoSurvey.Organizations.Infrastructure` and SQL Server integration tests. Production global context, migrations, API registration, provisioning, and tenant routing remain later tasks.

## How it works

`OrganizationConfiguration` implements EF Core's `IEntityTypeConfiguration<Organization>`. A context explicitly applies it to describe `organization.Organizations`, required columns, generated integer identity, unique PublicId/Code indexes, and approved SQL Server types/collation. Domain retains zero EF dependencies.

The Code converter writes `OrganizationCode.Value` and reads through `OrganizationCode.Create`. Record equality supplies value comparison without a custom comparer. Queries compare validated OrganizationCode values directly; translating `Code.Value` member access is not promised.

The private Organization constructor accepts all persisted values and generates nothing. Public `Create` validates input and supplies new identity/time values. Database reads preserve the existing identity and timestamp.

Conversion can normalize on read, so tests also inspect raw stored code. Binary collation is not domain validation. Direct SQL can bypass domain rules; canonical-code and UTC-offset CHECK constraints remain outside this slice. Unique indexes do not enforce authorization or historical immutability.

## Dependencies

- Mapping: `Microsoft.EntityFrameworkCore.Relational` 10.0.9.
- Test provider: `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9.
- Test-only explicit `System.Security.Cryptography.Pkcs` 9.0.21 patches SqlClient's vulnerable transitive 9.0.4 dependency. See [Microsoft's high-severity advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [moderate advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).

## Run locally

Prerequisites: repository .NET SDK, Windows PowerShell 5.1 or PowerShell 7, and Docker running Linux containers with sufficient resources for SQL Server. The runner uses SQL Server 2022 Developer for development/testing only and accepts its EULA when starting the container.

From the repository root:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
./scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
```

The runner generates a temporary password, starts a uniquely named container, binds a random loopback port, and supplies `GEOSURVEY_TEST_SQLSERVER` to child tests. It restores previous environment values and removes its container in `finally`. No host volume is mounted. Do not print the connection environment or container inspection output containing credentials. Abrupt termination can prevent cleanup; remove only the specific container left by that invocation.

The fixture waits for readiness, creates a randomly named database, and deletes only that database after successful initialization. `EnsureCreated` is test setup, not production migration management. Missing connection configuration fails tests with instructions instead of silently skipping. The runner executes the entire backend suite; CI uses the same command after building.

The `mcr.microsoft.com/mssql/server:2022-latest` image tag can change; it is not a pinned production deployment image. Hosted CI remains unverified until changes are committed/pushed and a run is observed.

## Coverage

- Fresh-context reads preserve ID, PublicId, code, display name, timestamp precision, and UTC offset; raw storage retains normalized code.
- Code equality queries translate; equivalent record instances do not cause updates.
- Duplicate display names are allowed.
- Concurrent duplicate codes yield one successful insert and one unique-constraint rejection.
- Duplicate PublicIds are rejected by SQL Server.
- Invalid stored codes fail materialization.
- Metadata matches approved schema, types, required properties, generation, indexes, and collation.

Identity mappings, cross-module FK composition, global migrations, tenant isolation, and runtime authorization require separate implementations and tests.

## Local verification — 2026-11-13

Executed on Windows with Windows PowerShell 5.1, .NET SDK selected by `global.json`, and Docker Linux containers:

| Command | Observed result |
| --- | --- |
| `dotnet restore GeoSurvey.sln` | Passed after adding the test-only patched cryptography dependency; final restore reported no warnings |
| `dotnet build GeoSurvey.sln --configuration Release --no-restore` | Passed, zero warnings and zero errors |
| `dotnet format GeoSurvey.sln --verify-no-changes --no-restore` | Passed |
| `dotnet test GeoSurvey.sln --configuration Release --no-build --no-restore --filter 'FullyQualifiedName!~GeoSurvey.Organizations.Infrastructure.Tests'` | Existing 87 tests passed while downloading SQL Server; new database tests were deliberately excluded in this preliminary run |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1` | Full suite passed: Organization Domain 38, Identity 35, Architecture 13, API 1, Organization SQL Server 7; 94 total, zero failures/skips |
| PowerShell parser validation | Passed for the runner |
| `git diff --check` and separate new-file whitespace review | Passed |

The runner exited successfully and no `geosurvey-tests-` containers remained afterward. Downloaded SQL Server image digest: `sha256:4525e3efac9f3e241cb003bb2ff2a82818b502f37e12049d788b1a9fa7e13b7f`. The first restore/build exposed NU1902/NU1903 warnings for transitive Pkcs 9.0.4; these were resolved by the documented 9.0.21 override, followed by a clean restore/build. Docker initially required startup/access outside the restricted sandbox.

Frontend, mobile, and hosted CI were not run for this slice. The workflow was updated to use the shared test runner; local execution does not prove the Ubuntu hosted run. No commit or push was performed.
