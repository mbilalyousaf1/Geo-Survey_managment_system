# API control-plane registration and local configuration

Implementation and verification date: 2026-11-16.

The API now references ControlPlane Infrastructure and registers `GlobalControlPlaneDbContext` with a scoped lifetime. One service/request scope reuses its context; separate scopes receive separate instances. Registering a context describes how DI should construct it, without opening a SQL connection or applying migrations.

## Registration and startup behavior

`AddGlobalControlPlanePersistence` is the API composition-root helper. It registers the approved Identity schema/key options and configures the context through the existing shared SQL Server helper. A new non-generic helper overload supports EF's standard `AddDbContext` callback while retaining the same validation/history settings as the existing typed overload.

The helper uses `ValidateOnStart` on connection options so bad configuration stops host startup. It reads `ConnectionStrings:GlobalControlPlane`, rejects missing/whitespace or unparsable SQL Server connection data, and requires explicit Server and Database fields to avoid implicit targets. Parser errors are replaced by a fixed validation message with no supplied values or inner parser exception. The configuration class is internal and is not an API response model.

Validation is syntactic: it does not check connectivity, credentials, database existence, permissions, or certificates. Valid configuration for an unavailable SQL Server still allows startup and `/health/live`. That endpoint reports process liveness, not database readiness. There is no database readiness endpoint in this slice.

The API does not register Identity login/user-manager services or expose an unrestricted persistence capability to Application services. No automatic `Migrate`, `EnsureCreated`, seeding, authentication, tenant resolution, or provisioning is added. Connection data comes from trusted server configuration, never request input.

## Development user-secrets

The API project has `UserSecretsId` `geosurvey-api-development`. ASP.NET Core loads that store in the Development environment. User-secrets live outside the repository and are not encrypted; this is a development facility, not production secret management. [Microsoft guidance](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)

To supply your existing local development connection without putting its value in command arguments/history, run this manually from repository root:

```powershell
$secureConnection = Read-Host 'Local control-plane SQL Server connection string' -AsSecureString
$connection = [Net.NetworkCredential]::new('', $secureConnection).Password
try {
    @{ 'ConnectionStrings:GlobalControlPlane' = $connection } |
        ConvertTo-Json -Compress |
        dotnet user-secrets set --project src/Backend/GeoSurvey.Api
    if ($LASTEXITCODE -ne 0) { throw 'Could not save local connection configuration.' }
}
finally {
    $connection = $null
    $secureConnection.Dispose()
}

dotnet run --project src/Backend/GeoSurvey.Api --configuration Release --no-build --launch-profile http
```

The JSON goes directly to the tool's standard input. No actual secret store was modified as part of implementing/testing this task. Do not list or dump the secret store into shared logs. Supplying a connection does not create its database; use an independently approved local database/migration procedure when real database operations are needed.

## Environment configuration

ASP.NET Core maps environment variable `ConnectionStrings__GlobalControlPlane` to the same key; environment configuration overrides user-secrets. For a temporary process-scoped value, use a masked prompt and restore any previous value afterward:

```powershell
$previousConnection = $env:ConnectionStrings__GlobalControlPlane
$secureConnection = Read-Host 'Local control-plane SQL Server connection string' -AsSecureString
try {
    $env:ConnectionStrings__GlobalControlPlane = [Net.NetworkCredential]::new('', $secureConnection).Password
    dotnet run --project src/Backend/GeoSurvey.Api --configuration Release --no-build --launch-profile http
}
finally {
    $env:ConnectionStrings__GlobalControlPlane = $previousConnection
    $secureConnection.Dispose()
}
```

Restart the API after changing configuration. The runtime key is separate from `GEOSURVEY_DESIGNTIME_SQLSERVER` used by EF tooling and `GEOSURVEY_TEST_SQLSERVER` used by disposable integration tests.

## Docker impact

The new API project reference requires the Docker restore/publish stages to include ControlPlane, Identity Infrastructure, Organizations Domain, and Organizations Infrastructure. The build-context allowlist now includes precisely those additional projects while continuing to exclude test files, build output, local settings, user-secrets, and credential files. No connection value is copied into an image layer.

Compose forwards the host's `ConnectionStrings__GlobalControlPlane` value. An absent value results in the same startup validation failure. Development user-secrets on the host are not automatically available inside the Production container. Use a connection address appropriate inside that container when supplying real configuration.

Use `docker compose config --quiet` for configuration syntax validation; rendered configuration can contain connection data. `docker compose build api` builds the image without starting or replacing an existing container. Running a new/updated application container is a separate operator action and still does not apply migrations.

## Verification

Executed locally:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Integration/GeoSurvey.Api.IntegrationTests/GeoSurvey.Api.IntegrationTests.csproj --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
docker compose config --quiet
docker build --file deploy/docker/GeoSurvey.Api.Dockerfile --tag geosurvey-api:registration-check .
git diff --check
```

Restore/build passed; Release build had zero warnings/errors. Formatting passed. All 161 backend tests passed, zero failures/skips: API 8, ControlPlane 25, Identity integration 35, Organization integration 7, Identity unit 35, Organization unit 38, Architecture 13.

The API's eight tests cover liveness with unreachable SQL Server, same-context reuse within a scope, different contexts across scopes, shared Identity/history/model settings, and startup rejection for missing/blank/unparsable/missing-target configuration. Invalid test input includes a synthetic password marker; exception text does not echo it or the invalid parser value. Test hosts explicitly override configuration and use the Testing environment rather than relying on developer user-secrets.

Compose validation and the review image build passed. A uniquely named temporary container from `geosurvey-api:registration-check`, with a random loopback HTTP port and an explicit credential-free unreachable SQL connection supplied through the environment, returned HTTP `200 Healthy`; `id -u` returned `1654`. That container was removed afterward. This checks packaging and database-independent liveness, not a real SQL connection. No existing container was restarted.

Hosted CI, frontend, mobile, real local database connectivity, and real user-secrets loading were not executed. No actual developer secret values were read or written. Production configuration/deployment and first-administrator bootstrap remain separate work. Nothing was committed or pushed.
