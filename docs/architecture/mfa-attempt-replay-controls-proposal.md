# Persistent MFA attempt and replay controls

Status: Accepted and implemented bounded controls slice, 2026-11-17. The owner selected five failures within 15 minutes and a 15-minute block. See [ADR 0035](../decisions/0035-mfa-attempt-replay-controls.md) and [implementation evidence](../development/mfa-attempt-replay-controls.md).

## Proposed account policy

For Platform Administrators, count failed codes across enrollment/verification challenges for the same account. Approved policy: five failures in a 15-minute window cause a 15-minute MFA block starting at the threshold failure. These are owner-selected application policy values, not values prescribed by the specification or RFC.

Only requests with a valid, password-verified restricted MFA challenge may affect the account counter. Malformed/incorrect codes under that valid challenge count as failures. Unknown, expired, consumed, revoked, wrong-account, or wrong-stamp challenges cannot change counters or prolong a block. Requests during a block do not extend it. A new challenge, application restart, password/security-stamp change, or pending-secret replacement must not clear the account-level counter/block. Block expiry resets the failed-attempt budget; below the threshold, expiry of the 15-minute failure window starts a new window.

At threshold, revoke outstanding MFA challenges for the account. Challenge issuance and pending-enrollment access must honor the persistent block. After expiry require a new password-verified challenge. Keep this MFA budget separate from password lockout; do not silently choose organization-user or recovery-code limits.

## Replay enforcement

Persist the latest accepted TOTP step for the account. An already accepted or older step cannot be accepted again, even with another challenge or concurrent request. Only a trusted verifier supplies the matched step; clients cannot assert success or choose an arbitrary counter. Retain replay state across challenge replacement. Reset/credential replacement behavior remains part of its separate approved lifecycle.

The eventual completion transaction must validate the current challenge/account, enforce budget and matched-step monotonicity, consume the challenge, reset failures on success, and commit confirmed/authenticated state together. A preliminary validity check, Boolean passed by a client, or separate check-then-save is insufficient. Storage controls alone do not authenticate or enable MFA.

## Implementation boundary

Add Identity-owned persistent verification-state mapping/contract and SQL implementation in ControlPlane Infrastructure, with one account-associated row, UTC timestamps, constraints, migration/review SQL, and no secrets/raw codes. Serialize with the existing account-first lock order and conditional updates. Update challenge issuance/eligibility and pending access to honor active blocks. Introduce no new package or endpoint. Confirmed-secret/recovery-code storage and public confirmation/login flows still require their reviewed implementation.

Tests must cover threshold boundaries, window/block expiry, no block extension, challenge replacement/restart persistence, wrong-challenge denial, atomic concurrent increments, repeated/older-step denial, concurrent acceptance with exactly one winner, and rollback. Documentation and existing schema-count assertions change with the migration.

The owner approved these controls and selected the concrete policy before implementation. The TOTP primitive remains stateless; callers must use the persistent controls inside a trusted completion transaction. Public confirmation/login and confirmed-secret/recovery-code workflows remain separate implementation work.
