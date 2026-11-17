# Restricted MFA challenge storage

Approved scope: [ADR 0030](../decisions/0030-restricted-mfa-challenges.md).

`MfaChallengeService` creates short-lived opaque tokens and hashes them before calling `IMfaChallengeStore`. The interface lets Identity define what persistence it needs without depending on the composed ControlPlane context. `EfMfaChallengeStore` supplies real SQL persistence, eligibility checks, serialized replacement, and atomic consumption. `TimeProvider` makes expiry tests deterministic without sleeping.

`identity.MfaChallenges` stores the account FK, purpose, digest, security stamp, and UTC lifecycle timestamps. The third migration, `AddMfaChallenges`, adds this table without modifying existing account/bootstrap rows. The composed model has seven application tables, eight including migration history, and five NO ACTION foreign keys. See [review SQL](sql/add-mfa-challenges.sql). The bootstrap operator role receives no new MFA permissions.

No raw token is persisted. Sensitive EF parameter logging is rejected by the store; callers must also keep tokens/stamps out of their own logs and traces. The ticket deliberately has no generated token-bearing `ToString`. Returning or consuming a ticket does not prove a password or authenticator code was verified. There is no API registration, login endpoint, TOTP verification, or JWT issuer in this slice.

The future trusted MFA completion workflow must perform its state changes and nonce consumption in the same database transaction. It must fail the transaction if consumption fails. `IsValidAsync` is only a preliminary check and cannot replace atomic consumption. Enrollment must consume while the account still satisfies the enrollment eligibility predicate, before committing the MFA-enabled state.

## Verification

Commands used:

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

SQL tests use disposable Docker SQL Server databases and real migrations. Coverage includes expiry boundaries, purpose/account/stamp binding, hashed storage, replacement, concurrent issuance/consumption, lockout/claim eligibility, and caller-transaction rollback. No application database migration, operator provisioning, commit, push, hosted CI, frontend, or mobile verification is performed here.

Final verification, 2026-11-17: Release build passed with zero warnings/errors. All 257 backend tests passed with zero failures/skips: ControlPlane SQL 50 (ten new MFA cases), Identity SQL 35, Organization SQL 7, Identity unit 82, Organization unit 38, console unit 24, architecture 13, and API 8. The design-time test also checks that the migration snapshot matches the model. Full formatting/analyzer verification and Git whitespace review passed. No packages changed, so restore was not repeated.

The preceding run passed all eight MFA cases present at that point, but failed the existing bootstrap lock-timeout test's 29-second elapsed-time assertion; it returned the expected LockUnavailable outcome earlier. Two additional MFA security cases were then added and the full suite rerun. The existing bootstrap code/test was left unchanged; its timing failure did not recur. An initial sandboxed build also exited unsuccessfully without diagnostics; builds with the required environment access passed.

Next small task: review TOTP secret protection/key handling before implementing authenticator enrollment. Rate limits, recovery, and authentication policy remain separate open decisions.
