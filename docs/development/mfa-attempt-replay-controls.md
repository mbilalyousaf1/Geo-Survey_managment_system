# Persistent MFA attempts and replay controls

Approved policy/design: [ADR 0035](../decisions/0035-mfa-attempt-replay-controls.md).

## Implementation and integration boundary

MfaVerificationState is an Identity Infrastructure persistence model. MfaAttemptPolicy holds the approved five/15-minute/15-minute constants. MfaVerificationService validates account/token/purpose input and passes a SHA-256 token digest to IMfaVerificationStore. The interface keeps the module independent of the composed context. It does not receive a raw code or claim to validate one; the future trusted workflow must use TotpAuthenticator and route invalid/malformed codes to RecordFailureAsync. Never expose a client-set success Boolean or matched step.

EfMfaVerificationStore uses the real global context and injected TimeProvider. It locks account then challenge base rows in the same order as pending storage, and reads UTC time after locks. It reuses EfMfaChallengeStore.ValidChallenges and its sensitive-logging guard. State is read without tracking; parameterized SQL/set-based updates avoid flushing unrelated tracked entities. Account locking serializes initial state insertion, concurrent failures, accepted-step updates, and challenge replacement. The updated shared account eligibility query excludes active blocks for both purposes, including pending-secret reads/writes and nonce consumption.

RecordFailureAsync commits its own transaction when none is supplied, or participates in the caller's transaction. It returns InvalidChallenge without modifying state for an ineligible challenge. Valid failures increment the fixed 15-minute window; failure five sets a 15-minute block and revokes outstanding MFA challenges atomically. Block expiry never resurrects revoked challenges. Requests during the block do not prolong it. A valid attempt at/after window or block expiry resets the failure budget while retaining LastAcceptedStep.

TryAcceptStepAsync requires an existing completion transaction. It checks the matched step remains inside current +/- one and is newer than persisted replay state. Rejections under a valid challenge increment the failure budget in that transaction. Success consumes the challenge, stores the step, and clears failure/window/block fields. Returning true does not enable MFA or issue an access token. Public routes and confirmed-secret/recovery-code storage remain unimplemented, and these services are not registered in the API yet.

Caller rules: commit legitimate failed-attempt increments on denied codes/replay; roll back infrastructure errors; save confirmed/authenticated state in the same transaction as accepted-step success. Do not treat a preliminary IsValid result or raw low-level TryConsume result as completion. No control operation may be driven by an arbitrary client-supplied matched step. Reset/recovery and credential replacement must explicitly preserve/review these account-level controls.

## Schema

The fifth migration, AddMfaVerificationStates, adds AccountId (PK/FK), FailedAttemptCount, FailureWindowStartedAtUtc, BlockedUntilUtc, and LastAcceptedStep. It includes count/window/block/step CHECK constraints and NO ACTION deletion. Global schema totals are nine application tables, ten with migration history, and eight NO ACTION foreign keys. See [review SQL](sql/add-mfa-verification-states.sql). No real database migration or bootstrap grant change is performed. Existing accounts, pending ciphertext, challenge lifecycle, and bootstrap state are preserved on upgrade; no default state is seeded.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Migration/review SQL were generated with dotnet ef and the existing offline design-time configuration. The Release build passed with zero warnings/errors. All 370 backend tests passed, zero failures/skips: ControlPlane SQL 93, Identity unit 141, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 19. This includes 26 new SQL cases (25 control cases and one populated upgrade) and two new unit cases; the preceding standalone TOTP cases also ran in this full suite. Formatting/analyzers, new-document link checks, content/diff review, and Git whitespace checks passed. No failed build/test run occurred in this slice. No restore was repeated because no dependencies changed.

New SQL cases cover threshold and exact window/block boundaries, pending/challenge denial while blocked, no block extension, fresh-challenge requirement, budget persistence across replacement/stamp/purpose changes and new context instances, invalid challenge isolation, concurrent increments/acceptance, replay/older/out-of-window steps, success reset/consumption, history retention through block expiry, caller rollback, transaction requirement, SQL constraints, and migration from populated pending-enrollment storage. New unit cases check the digest-only boundary and input rejection. Generated step values in storage tests represent trusted verifier output; published-vector calculation tests remain separate in TotpAuthenticatorTests.

Tests use disposable Docker SQL Server databases. A new context is not a real process/container restart test. No real account, deployment, production certificate operation, authentication endpoint, physical authenticator app, hosted CI, frontend, mobile, commit, or push is performed. Next small review: confirmed authenticator-secret storage and atomic enrollment confirmation, with single-use recovery-code issuance/handling resolved before completing the public enrollment flow.
