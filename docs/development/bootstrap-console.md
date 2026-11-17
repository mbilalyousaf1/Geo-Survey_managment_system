# Bootstrap console and operator runbook

Implementation and verification date: 2026-11-17.

`src/Backend/GeoSurvey.Bootstrap` is the separate operator console approved in record 0026. It calls the existing transactional service; it does not expose registration through the API, migrate databases, create logins/roles, reset credentials, or enroll MFA. MFA enrollment/login remains unimplemented, so account creation alone does not complete Phase 1 or provide a usable administration session.

## Before an operator runs it

1. Verify the intended global database and apply its reviewed migrations through the separate deployment process.
2. Have the security administrator review/configure the dedicated GeoSurveyBootstrap database role and operator identity using the [permission runbook](bootstrap-creation-service.md). Do not use the API credential or an elevated database-owner identity for bootstrap. The tool does not grant its own permissions.
3. Run the tool from a trusted interactive terminal under authorized deployment access. It rejects redirected input or output and provides no unattended/password-argument mode.
4. Supply only the dedicated operator connection through `GEOSURVEY_BOOTSTRAP_SQLSERVER`. It requires explicit Server and Database and does not fall back to API user-secrets, API connection configuration, design-time settings, or the disposable test connection.

No actual operator credential or application database was configured while implementing this tool. Do not run it with real environment access merely to test its UI: a successful creation is permanently recorded.

## Build, help, and launch

From repository root:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet run --project src/Backend/GeoSurvey.Bootstrap --configuration Release --no-build --no-restore -- --help
```

For a deliberate operator run, read the connection without placing its value in command arguments/history, and restore the previous process environment afterward:

```powershell
$previousBootstrapConnection = $env:GEOSURVEY_BOOTSTRAP_SQLSERVER
$secureBootstrapConnection = Read-Host 'Dedicated bootstrap database connection string' -AsSecureString
try {
    $env:GEOSURVEY_BOOTSTRAP_SQLSERVER = [Net.NetworkCredential]::new('', $secureBootstrapConnection).Password
    dotnet run --project src/Backend/GeoSurvey.Bootstrap --configuration Release --no-build --no-restore
    $bootstrapExitCode = $LASTEXITCODE
    Write-Output "Bootstrap exit code: $bootstrapExitCode"
}
finally {
    $env:GEOSURVEY_BOOTSTRAP_SQLSERVER = $previousBootstrapConnection
    $secureBootstrapConnection.Dispose()
}
```

The console prompts for email, password, and confirmation. Email is displayed as typed; both password fields show only masks. Passwords never come from environment variables or arguments. NFC-normalized confirmation must match with case/whitespace significant. Invalid input or mismatch stops the attempt; the tool does not loop into automatic credential changes. Managed password strings exist in process memory; this implementation does not promise memory-erasure guarantees.

Enter submits a field, Backspace removes the last code point (including a complete surrogate pair), Escape cancels input, and Ctrl+C requests cancellation during input or async database work. Native input uses intercepted Console.ReadKey and polls key availability with the cancellation token. Terminal control keys are input controls, not extra credential transformations. Password normalization/length/blocklist rules remain in the existing components.

## Exit codes and outcomes

| Code | Meaning |
| --- | --- |
| 0 | Creation succeeded, or help was displayed |
| 1 | Operational failure or unrecognized service outcome; no raw exception is printed |
| 2 | Unsupported arguments, noninteractive terminal, missing/invalid dedicated connection, invalid input/password, or confirmation mismatch |
| 3 | Permanent completion already exists; no replacement/reset |
| 4 | An administrator already exists; no adoption/reset |
| 5 | The Platform account email already exists; no promotion/reset |
| 6 | Bootstrap lock unavailable; review other operator activity before retrying |
| 130 | Canceled |

The only accepted arguments are `--help` or `-h`, alone; all others are rejected without echoing them. Configuration/parser/service errors are replaced by fixed safe messages. A canceled/interrupted commit can have an uncertain outcome; retry consults permanent completion and must not be treated as credential reset. The success message states that MFA is still required; it does not claim enrollment is implemented.

## Important classes and boundaries

- `Program` owns the process cancellation token and removes its Ctrl+C handler on exit.
- `BootstrapCommand` validates the operator configuration, prompts/confirms, builds the approved Identity/context options in a short-lived service provider, delegates to `FirstPlatformAdministratorBootstrapper`, and maps outcomes to safe messages/codes. It contains no account-table manipulation.
- `IBootstrapTerminal` is the input/output boundary. `SystemBootstrapTerminal` implements real console behavior; deterministic key-event tests can exercise masking without storing real passwords or replacing the creation service with a fake success.
- `BootstrapInput` handles intercepted key events and normalized confirmation. It returns strings rather than password records with generated printable representations.

The console references ControlPlane Infrastructure only, with existing transitive dependencies. The new unit project reuses the existing test SDK/xUnit versions. No new package version, migration, API reference to the console, Docker exposure, or authentication endpoint is added. The root solution/CI includes the projects; the API Docker build-context allowlist excludes the operator project.

## Publish

Publish to a new destination and retain the complete output, including ThirdPartyNotices and the Identity assembly's embedded corpus:

```powershell
dotnet publish src/Backend/GeoSurvey.Bootstrap/GeoSurvey.Bootstrap.csproj --configuration Release --no-restore --output <new-output-directory>
dotnet <new-output-directory>/GeoSurvey.Bootstrap.dll --help
```

This is a framework-dependent .NET 10 tool; install the compatible runtime on the operator machine. Publication is not installation, privilege provisioning, or permission to run it against an arbitrary database.

## Verification evidence

Executed: restore, Release solution build, `dotnet test tests/Unit/GeoSurvey.Bootstrap.Tests/GeoSurvey.Bootstrap.Tests.csproj --configuration Release --no-build --no-restore`, the full `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1`, and `dotnet format GeoSurvey.sln --verify-no-changes --no-restore`.

Restore and build passed, with zero build warnings/errors. Console tests: 24 passed. Full backend: 247 passed, zero failures/skips (Console 24, ControlPlane 40, Identity unit 82, Organization unit 38, Architecture 13, API 8, Identity integration 35, Organization integration 7). Formatting passed. The existing SQL integration tests still verify real restricted-role transactional creation; console tests do not substitute a fake creation service.

Actual process smoke checks also passed:

- `--help` returned 0 without configuration or database access.
- Redirected input/output was rejected with 2 before credentials were read.
- In a real Windows pseudo-terminal using an explicit credential-free unreachable SQL address, Escape at the email prompt returned 130.
- In another real terminal run, synthetic email plus two different synthetic passphrases displayed only asterisks for password fields and returned mismatch code 2 before the service was invoked. No connection/account creation occurred.
- Publication to a new ignored TestResults/bootstrap-console-publish-GUID directory included the console DLL/runtime config, Identity assembly, corpus license, and provenance. Running the published DLL with `--help` succeeded.

No actual operator credential, login, role grant, or application database was created/changed. Real console-success creation against a deployment database was not run; native Linux/macOS interactive behavior, hosted CI, API Docker rebuild, frontend, and mobile were not verified. The backend runner created only its usual disposable SQL Server test data. Nothing was committed or pushed.

## Next Phase 1 work

Design restricted MFA enrollment/login sessions, TOTP secret protection, recovery-code behavior, and authorization before implementing authentication endpoints. Organization creation/provisioning, tenant resolution, onboarding, and end-to-end acceptance remain later Phase 1 tasks.
