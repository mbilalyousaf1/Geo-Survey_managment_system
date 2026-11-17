# Transactional first-administrator creation service

Implementation date: 2026-11-17.

Subsequent integration: the [operator console](bootstrap-console.md) now wraps this service with masked confirmation, separate environment configuration, cancellation, and tested exit codes. Earlier pending-console statements below describe the original service slice. MFA enrollment/login remains pending.

This slice implements `FirstPlatformAdministratorBootstrapper`, its result enum, shared administrator-claim constants, and an operator-reviewed permission script. The console remains unimplemented, and the service is not registered in the API. Accounts/claims/markers created during verification exist only in disposable SQL Server test databases.

## Service flow and important types

`CreateAsync(email, password, cancellationToken)` validates email through the existing account factory and normalizes/checks the password through the packaged local corpus. It applies shared Identity V3 hashing options before opening a transaction. It initializes a fresh random security stamp; EmailConfirmed and TwoFactorEnabled remain false. Creation is not MFA completion or permission to administer the platform.

The constructor accepts trusted, operator-configured `DbContextOptions<GlobalControlPlaneDbContext>`. Every attempt creates its own context. No request-supplied connection or shared EF context is used. The service disables EF ILogger payload logging and sensitive-data logging for its private context, because SQL Server exceptions can contain duplicate values even when parameter logging is disabled. SQL/EF write failures become a fixed safe operational exception without an attached SQL exception. Callers must not add password-bearing diagnostic/interceptor logging or print input.

Within a ReadCommitted transaction:

1. Require membership in database role `GeoSurveyBootstrap`.
2. Acquire an Exclusive, transaction-owned `sys.sp_getapplock` for fixed resource `GeoSurvey.FirstPlatformAdministratorBootstrap`, using that role as the database principal. Lock timeout is 30000 ms; the SQL command timeout is 45 seconds. Negative acquisition results trigger explicit rollback and return LockUnavailable, unless cancellation is requested.
3. Check any completion marker first, then an existing Platform account with the accepted administrator claim. Locked/incomplete accounts are not excluded. Check a Platform-scoped normalized-email conflict; never adopt/promote/reset an existing account.
4. Save the new account to obtain its database key, then save its administrator claim and completion marker, all inside the same transaction.
5. Commit once. Guard exits and exceptions await disposal of the uncommitted transaction, rolling back and releasing its application lock. Hashing and interactive input do not occur while holding the lock.

`BootstrapCreationResult` carries only Created, AlreadyCompleted, AdministratorAlreadyExists, AccountAlreadyExists, or LockUnavailable; it contains no password, hash, email, or internal ID. `PlatformAdministratorClaims` supplies the accepted server-managed claim type/value. No new interface is invented for this single operational entry point; its method is the narrow contract for the future console.

Cancellation is accepted throughout async database work and checked before/after hashing; hashing itself is synchronous framework work. Tests cover precancellation, not every possible mid-command/network interruption. A lost commit acknowledgement can leave an uncertain client outcome; retry must consult permanent completion and must never reset credentials. The service does not promise that every connectivity failure proves no commit occurred.

## Permission script and operator setup boundary

`deploy/sql/bootstrap-role.sql` is a separate administrator-run script, not a migration or application-startup action. It requires the three tables to exist, creates/validates the named database role, grants SELECT/INSERT on Accounts, AccountClaims, and BootstrapCompletion, and explicitly denies UPDATE/DELETE on BootstrapCompletion. It grants no login, membership, schema/security modification, or credential-reset permission and contains no credential value.

Before a real operation, a deployment security administrator must separately:

1. Select and verify the intended migrated global database using controlled administrative configuration.
2. Review/run the permission script in that database. It does not revoke or sanitize pre-existing additional grants or memberships; inspect any existing role and the operator's effective permissions.
3. Provision a dedicated operator database user/credential using the deployment's approved authentication mechanism and grant membership in GeoSurveyBootstrap. Do not use the API runtime identity or an elevated db_owner/sysadmin identity for the tool. Credential provisioning/delivery is not automated by this slice.
4. Ensure the API runtime identity is not a member of the bootstrap role and cannot mutate the completion marker. Do not grant runtime bootstrap-specific privileges.
5. Keep migrations/security administration separate from bootstrap execution. The restricted bootstrap role cannot install missing schemas, create users/roles, alter tables, reset account credentials, or clear completion under its supplied grants.

Application locks serialize cooperating service calls; they do not prevent arbitrary direct SQL writers or privileged administrators from bypassing the workflow. Broad grants, role ownership, database ownership, or privileged deployment access can exceed these permissions. The script is not a universal row-level platform-scope authorization policy for every database writer. The existing role is reviewed rather than silently stripped of permissions belonging to existing work.

No real database role or user was configured during implementation. The only role setup executed was inside disposable tests; the console and concrete operator credential-delivery/run commands follow separately.

## SQL Server tests

Nine new cases cover:

- Complete account/hash/security-stamp/claim/marker persistence using the restricted role, without email/MFA confirmation.
- Two concurrent attempts producing exactly one Created and one AlreadyCompleted result with one account/claim/marker.
- A temporary test-only claim CHECK constraint forcing failure after the account insert; all rows roll back and a retry succeeds after removing the constraint. A recording logger verifies that service EF payload logging stays disabled even if caller options request verbose/sensitive logs.
- Reruns preserve existing hash/security stamp/public ID; deleting the synthetic account/claim with the test administrator does not allow replacement because completion survives.
- A pre-existing locked-out administrator without a marker is not replaced or silently adopted.
- An existing non-admin Platform email is not promoted/reset.
- Non-members cannot bootstrap; the authorized restricted user cannot update/delete the marker, update accounts, or create schema objects.
- A real held SQL application lock causes the configured roughly 30-second wait, writes nothing, releases cleanly, and allows a later successful attempt.
- A blocked password and a precanceled attempt cause no writes.

`BootstrapServiceDatabase` reuses the guarded migration-fixture lifecycle but creates a fresh database per test. It applies the permission script and creates database-only users WITHOUT LOGIN. A test-only connection interceptor executes SQL Server EXECUTE AS USER to exercise actual permission tokens; no fake production authorization is used, no server logins/passwords are created, and no deployment authentication handshake is claimed. Cleanup deletes only the generated database and then disposes its service provider; the existing runner removes its temporary container.

## Verification commands

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

The permission SQL is copied to test output through a test-project item; there are no new package references, migration/model changes, or API registrations. Restore was not repeated because dependencies did not change. Hosted CI, Docker API-image rebuild, frontend, mobile, actual operator credential provisioning, and console execution were not performed. No real administrator or application database was modified; nothing was committed or pushed.

## Local verification — 2026-11-17

Final Release build: zero warnings/errors. Final full backend run: 223 passed, zero failures/skips (ControlPlane 40, Identity unit 82, Organization unit 38, Architecture 13, API 8, Identity integration 35, Organization integration 7). Formatting passed after fixing two initializer-layout issues. The full suite was repeated after adding private-context EF logging suppression and its recording-logger assertion. Both test runs passed; the final run includes that privacy correction.

The real lock-timeout test contributes a roughly 30-second wait; the final ControlPlane project run took about 66 seconds including database creation, hashing, permissions, and cleanup. No artificial reduced production timeout was used. Operator setup and synthetic records existed only in disposable databases. Diff/new-file whitespace and documentation-link checks completed separately; temporary-container cleanup was verified.

## Related documentation

- [Console/operator access decision](../decisions/0026-bootstrap-console-and-operator-access.md)
- [Transaction/lock decision](../decisions/0022-bootstrap-transaction-and-lock.md)
- [Permanent completion storage](bootstrap-completion-storage.md)
- [Password blocklist](local-password-blocklist.md)
- [Phase 1 plan](phase-1-completion-plan.md)
