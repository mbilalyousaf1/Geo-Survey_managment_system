# Confirmed-factor sign-in verification

Approved design: [ADR 0038](../decisions/0038-platform-mfa-verification.md).

## Flow and boundaries

IPlatformMfaVerifier accepts a Verification token and submitted code, without a client account/tenant identifier, secret, matched step, or success flag. PlatformMfaVerifier hashes the token, finds its stored public account association, and then locks account/challenge base rows in the existing order. The first lookup only determines where to revalidate: changes in stored ownership between routing and locking cannot authorize the previously resolved account.

The current challenge must pass the existing scope/claim/stamp/purpose/expiry/lockout controls. A direct additional active LockoutEnd check preserves the approved password policy even if a legacy flag is disabled; no shared helper behavior or password field is modified. The confirmed encrypted secret and issued recovery rows must exist. Consumed recovery rows remain issued data. Enrollment tokens, invalid current state, or unknown/malformed tokens return the same empty rejection and do not spend another account's budget.

The verifier decrypts the confirmed secret for the resolved account and checks canonical secret shape before real TOTP verification. Malformed/wrong codes under an eligible challenge call RecordFailureAsync. A valid calculation still needs atomic accepted-step enforcement and conditional challenge consumption through TryAcceptStepAsync. Same-step reuse under a fresh password challenge is rejected and counted. Infrastructure/decryption errors fail closed and must cause outer rollback; they are not code failures. SQL/update errors are sanitized without inner provider details. The existing sensitive-data-logging guard rejects unsafe context configuration; never add logging of tokens, codes, ciphertext, or results.

The verifier requires a caller-owned ReadCommitted transaction and never commits/rolls back it. ReadCommitted allows the preliminary token/account routing statement to release its read locks before account-first update locking. Other isolation modes are deliberately rejected. Reuse one scoped global context for this completion unit of work; do not share it across tenants/requests or independently commit helper contexts.

PlatformMfaVerificationResult contains only IsVerified and nullable AccountPublicId. It has no generated field-bearing ToString and no writable instance properties. A verified result is provisional until outer commit and must be discarded after rollback. It is neither an authenticated principal nor a client proof token. The future session coordinator must create session/refresh state within the same transaction before committing/delivering credentials. On denied valid code/replay outcomes it must commit the failure counts; on infrastructure/cancellation errors it must roll back. This caller responsibility is explicit: the verifier cannot force an outer caller to commit a denied attempt correctly.

No password counter, user stamp, confirmed credential, or recovery code is altered on successful TOTP verification. Only the nonce and existing MFA control state change. There is no new table, package, API route, host registration, JWT/cookie/refresh token, session, reset, or recovery-code consumption implementation in this slice.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

The Release build passed with zero warnings/errors. The targeted Identity run passed all 161 cases, including two new contract/result cases. The full backend run passed 487 tests, zero failures/skips: ControlPlane SQL 190, Identity unit 161, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 19. This adds 32 SQL cases and two unit cases. Formatting/analyzers, new-document link review, code/diff review, and Git whitespace checks passed. The initial SQL runner stopped before executing tests because Docker's Linux engine pipe was absent; Docker Desktop was started through its installed CLI, engine readiness was verified, and the unchanged runner was retried successfully. No application test or build failed in this slice. No dependency or schema changed, so restore/migration generation was not repeated; existing snapshot/migration cases ran in the full suite.

The new SQL tests create real password hashes and exercise password entry, actual enrollment confirmation, then a later published RFC step for sign-in. They cover consumed backups, wrong/malformed codes, five-failure blocking, post-expiry fresh challenges, role/scope/stamp/current-state/purpose/lifetime denial, missing factors, decryption/shape errors and cross-account ciphertext, account resolution without client IDs, replay, one-winner concurrency, caller rollback after simulated downstream SQL failure, rollback of denied-attempt updates, required isolation/transaction, cancellation, unsafe logging, lock-wait expiry, changed stored ownership after routing, and sanitized SQL-control errors. The downstream error represents an outer persistence failure; no session implementation is faked or claimed complete. Real SQL tests use disposable Docker databases and test-only ephemeral protection keys.

No real administrator, physical authenticator-app test, production deployment, real process/container restart, hosted CI, frontend/mobile, commit, or push is performed. Next review: session/access-token/refresh-token contract and server-side authorization, followed by separately approved public API/throttling and recovery authentication.
