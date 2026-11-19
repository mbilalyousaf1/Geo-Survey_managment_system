# Internal MFA enrollment confirmation

Approved design: [ADR 0036](../decisions/0036-mfa-enrollment-confirmation.md).

## Components and behavior

IMfaEnrollmentConfirmation is the Identity-owned contract for the trusted future enrollment flow. MfaEnrollmentConfirmationService lives in ControlPlane Infrastructure and owns the SQL transaction. Its inputs are an account PublicId, enrollment token, and submitted code. The future caller must resolve the account server-side and first verify the password before issuing that restricted challenge. It must not pass client assertions of identity/authorization as proof.

The workflow uses the existing challenge eligibility and account-first/challenge-second locking. The verification store's LockAsync changed from private to internal within ControlPlane Infrastructure solely for this reuse; no new public persistence capability is exposed. UTC time and eligibility are checked after waiting for locks. The pending row must match both account and challenge, and there must be no confirmed credential/recovery rows already associated with the account. These checks prevent replacing an existing factor or adopting stale partial state.

The service decrypts the pending secret using ITotpSecretProtector and verifies the actual code with TotpAuthenticator. Invalid/malformed code results commit RecordFailureAsync, including threshold revocation/blocking. Valid but replayed codes commit the control-store rejection outcome. Expired/revoked/consumed/wrong-account/wrong-stamp/ineligible challenges return rejection without changing counters. Missing pending material also rejects without consuming the challenge. Unreadable ciphertext or a decrypted noncanonical secret raises a fixed cryptographic error and rolls back rather than charging user failures.

On success, the existing control store consumes the challenge and saves the matched step in the same transaction. The workflow writes the confirmed ciphertext/timestamp and recovery hashes, enables MFA, rotates security/concurrency stamps, revokes other outstanding MFA challenges, and deletes the transferred pending row. Direct parameterized/set-based operations avoid flushing unrelated tracked entities. Existing tracked account objects are not refreshed by these operations: future callers must reload state or use a fresh context, and must not save a stale tracked account over the result.

Recovery codes are generated only after code/replay acceptance and are returned only after commit. The service rejects an ambient transaction rather than exposing a batch whose outer commit might fail. No plaintext secret, code, or provisioning URI is persisted. MfaEnrollmentConfirmationResult is a non-record with a defensive read-only batch copy and default non-disclosing ToString. This does not make it safe to destructure/log/trace/serialize as diagnostics: raw codes must only reach the intended one-time delivery channel when the future API is reviewed. Managed strings cannot be reliably erased; mutable random/hash buffers are cleared.

## Recovery issuance boundary

GenerateBatch issues ten independent 128-bit values as eight uppercase four-hex-digit groups. ComputeIssuedCodeDigest uses a versioned purpose, account GUID binding, and the ungrouped canonical code. Canonical issued formatting is validated by this issuance helper; recovery login input normalization is not chosen here. Code hashes use binary collation, and no raw-code redisplay, reissue, consumption, reset, or recovery authentication operation is added.

If the committed result is lost, repeating confirmation returns no batch and does not change the configured factor. Production operations must not automatically retry ambiguous commits to manufacture new codes. The configured authenticator can be used once the separate login workflow exists; recovery-code regeneration and administrative recovery remain unimplemented. Nullable ConsumedAtUtc is storage for the future atomic single-use workflow, not evidence that recovery login is ready.

## Schema and deployment

The sixth migration, AddConfirmedMfaEnrollment, adds identity.TotpCredentials (AccountId PK/FK, ProtectedSecret, ConfirmedAtUtc) and identity.MfaRecoveryCodes (AccountId + CodeHash composite PK, CreatedAtUtc, ConsumedAtUtc). Both account FKs are NO ACTION; consumed time cannot precede creation. Global totals are eleven application tables, twelve with migration history, and ten NO ACTION foreign keys. See [review SQL](sql/add-confirmed-mfa-enrollment.sql). Existing pending/verification/challenge/bootstrap data is preserved; nothing is seeded or confirmed by migration.

The workflow is not registered in the API yet: no password-verification entry point, enrollment-generation endpoint, public confirmation UI/API, session token, or recovery login is enabled by this task. Use the existing persistent PFX-backed key host before any real deployment; losing the required keys still makes both pending and confirmed ciphertext unreadable. No production migration, grant, certificate, or administrator is created here.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Migration and review SQL were generated with dotnet ef using the existing offline design-time connection, without contacting an application database. The Release build passed with zero warnings/errors. The targeted Identity run passed all 151 cases, including ten new recovery-issuance/result cases. The full backend run passed 409 tests, zero failures/skips: ControlPlane SQL 122, Identity unit 151, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 19. This adds 28 confirmation/storage SQL cases, one populated upgrade case, and ten unit cases. Formatting/analyzers, new-document link review, code/diff review, and Git whitespace checks passed. No build/test failure occurred in this slice. No restore was repeated because no dependency changed.

New SQL tests use disposable SQL Server databases, real Data Protection, and published RFC secret/code data. Coverage includes actual TOTP confirmation with preserved leading zero, ciphertext/digest-only storage, timestamps and stamp rotation, other challenge revocation, unchanged password/scope/claims, no reissue, failure budget/lockout, malformed/wrong codes, expiry/replacement/stamp/account/organization-scope denial, missing/conflicting state, unreadable/noncanonical secret failure, replay, one-winner concurrency, expiry while waiting for a lock, ambient-transaction/cancellation rejection, and new SQL constraints. A late injected persistence failure occurs after all ten recovery inserts and account-stamp update, proving rollback restores pending state, nonce/replay state, flags/stamps, and permits a subsequent valid retry. The populated fifth-schema upgrade preserves pending/control data and seeds neither confirmed credentials nor recovery codes.

No production enrollment, real administrator, physical authenticator-app test, hosted CI, frontend/mobile, actual process/container restart, commit, or push is performed. Next small review: Platform Administrator password verification and restricted MFA entry flow, plus remaining session/API design and single-use recovery-code authorization/consumption policy before public authentication completion.
