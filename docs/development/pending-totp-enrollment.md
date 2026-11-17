# Protected pending authenticator enrollment

Approved decision: [0033](../decisions/0033-pending-totp-enrollment-storage.md).

## Components and behavior

PendingTotpEnrollmentService accepts trusted secret material, protects it with the existing account-bound protector, and passes only ciphertext and a SHA-256 token digest through IPendingTotpEnrollmentStore. The contract keeps Identity independent of the composed context. ReadSecretAsync is an internal plaintext operation for the future trusted verification flow, not an HTTP response contract. It returns null when the challenge/pending association is invalid; corrupted/unreadable ciphertext raises the protector's fixed cryptographic failure. Do not log inputs, ciphertext, output secrets, provisioning URLs, or QR data.

EfPendingTotpEnrollmentStore reuses EfMfaChallengeStore.ValidChallenges, changed from private to internal within ControlPlane Infrastructure. There is no new public query boundary or broader module access. Writes lock the account first, then the challenge, and obtain UTC time after waiting for locks. The account lock serializes concurrent first writes and replacements; the challenge lock coordinates with consumption. Database constraints independently enforce ownership and one pending row per account. Single-query reads recheck eligibility and challenge association. Reads are preliminary: future completion must revalidate and consume atomically with confirmed state.

TryStoreAsync returns false for an ineligible challenge or an already-initialized same challenge. A repeat does not report a new secret as successfully stored. After challenge replacement, neither the revoked token nor the new token can read the old pending secret. The new valid token can store fresh ciphertext, replacing the pending row. MFA remains disabled. No account credentials, claims, security stamps, recovery codes, or challenge lifecycle flags are changed by the pending service.

The service/store are not registered in the API yet because there is no enrollment endpoint or trusted generator. No placeholder route or success response is introduced. The API's existing protector/key-host configuration remains unchanged. There is no authenticator generation, TOTP parameter choice, enrollment confirmation, reset, or login implementation here.

## Schema and migration

The fourth migration, AddPendingTotpEnrollments, creates identity.PendingTotpEnrollments and adds AK_MfaChallenges_Id_AccountId. Review [generated SQL](sql/add-pending-totp-enrollments.sql). AccountId is a non-generated PK/FK; ChallengeId is unique and participates in the composite challenge/account FK. ProtectedSecret is required nvarchar(max), and CreatedAtUtc is datetimeoffset(7). All new FKs are NO ACTION. The global model now has eight application tables, nine with migration history, and seven foreign keys. Identity-only module contexts remain unchanged.

Expiry/revocation makes pending material unusable but does not schedule deletion. Automatic cleanup, backup/retention, confirmed-secret storage, and recovery remain open. Bootstrap's restricted operator receives no new permissions. No real database migration was applied.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Migration and SQL review were generated through dotnet ef using the existing offline design-time configuration; no application database was contacted. Tests use real disposable SQL Server migrations and real Data Protection with test-only ephemeral keys. Unit test recording storage is a test double only; production storage is SQL-backed.

Coverage: ciphertext/digest-only persistence boundary, unreadable ciphertext, pre-cancelled writes, trusted round trip, no MFA enablement/challenge consumption, same-challenge refusal, replacement binding, concurrent first writes, expiry while waiting for locks, concurrent consumption, caller rollback, wrong account/token/purpose, changed stamp, removed claim, lockout, enabled MFA, and server-enforced challenge/account ownership. A populated third-schema upgrade preserves existing accounts/challenges/bootstrap state; fresh migrations and snapshot consistency are also checked.

Final verification, 2026-11-17: Release build passed with zero warnings/errors. All 299 backend tests passed, zero failures/skips: ControlPlane SQL 67, Identity unit 96, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 19. This adds 16 pending-enrollment SQL cases, one upgrade case, and three unit cases. Formatting/analyzers, new-document link review, Git diff review, and whitespace checks passed. Earlier runs passed 297 tests, then 299 after two added concurrency cases; the final 299-test run also verified the base-row locking refinement. No failed build or test run occurred in this slice.

No packages changed, so restore was not repeated. Hosted CI, frontend, mobile, real enrollment, production key access, process/container restart, deployment, commit, and push are outside this task. Next small review: authenticator generation/verification parameters, attempt/replay controls, and atomic confirmation with confirmed-secret storage and recovery-code handling.
