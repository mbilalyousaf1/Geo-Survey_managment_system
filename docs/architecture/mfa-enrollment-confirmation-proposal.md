# Atomic Platform Administrator MFA enrollment confirmation

Status: Accepted and implemented internal workflow, 2026-11-17. The owner approved confirmation and ten 128-bit recovery codes displayed once. See [ADR 0036](../decisions/0036-mfa-enrollment-confirmation.md) and [implementation evidence](../development/mfa-enrollment-confirmation.md).

## Behavior

Add an internal confirmation workflow that accepts a server-resolved account, restricted enrollment token, and submitted authenticator code. It must independently load the pending secret and perform real TotpAuthenticator verification. It must never accept a client-supplied success flag, matched step, secret, or account association as proof.

Use one account-first/challenge-second SQL transaction. Revalidate eligibility and pending association after acquiring locks; obtain current UTC time after waiting. Invalid/malformed codes under a valid challenge commit the existing failed-attempt update, including any threshold block/revocation. Invalid challenges change nothing. Unreadable ciphertext and infrastructure failures roll back and fail closed.

On a valid, unreplayed code, atomically accept the step and consume the challenge through the existing controls; store the confirmed account-bound ciphertext and confirmation UTC timestamp; persist the initial recovery-code digests; set TwoFactorEnabled; rotate the security/concurrency stamps; remove the now-transferred pending row; and revoke other outstanding account MFA challenges. Rotating the security stamp invalidates pre-confirmation challenges. No password hash, account scope, organization association, or administrator claim changes.

## Approved recovery-code issuance

Issue ten independently random 128-bit codes at first successful confirmation. Display as uppercase hexadecimal groups of four characters (32 hex characters plus separators). Store only SHA-256 digests bound to the account PublicId; record creation time and a nullable consumption timestamp for later atomic single-use handling. Do not persist raw codes, send them to logs, or provide a redisplay endpoint. Repeating confirmation cannot replace confirmed secrets or reissue codes.

Return raw codes only from the successful committed internal workflow in a result type without generated secret-bearing ToString. If the result is lost, enrollment stays committed; authenticator login remains possible. Do not silently reissue codes on retry. Recovery-code regeneration, password-plus-recovery-code authentication, reset, and administrative recovery are separate decisions; no consumption/authentication endpoint is included in this slice. Issuance alone does not complete the future single-use recovery workflow.

## Components, schema, and tests

- Identity Infrastructure: confirmed authenticator/recovery-code models and mappings, code generation/digest helper, confirmation contract/result, and service orchestration as appropriate to existing module boundaries.
- ControlPlane Infrastructure: transaction-owning confirmation implementation, composed mappings, migration/snapshot, review SQL, and narrowly scoped reuse of existing lock/control helpers.
- Tests: real TOTP confirmation, malformed/wrong code counts, five-failure block, expiry/replacement/account/stamp binding, replay rejection, concurrent confirmation with one winner, ciphertext/digest-only storage, no repeated issuance, security-stamp invalidation, and rollback after partial database writes.
- Documentation and existing migration/model-count assertions.

Use existing .NET cryptography and packages. Keep account/challenge/secret authorization on the server. Confirmed/recovery records use NO ACTION account FKs and UTC timestamps. This is internal backend workflow integration only: no public API, UI, JWT, signin session, recovery login, or real administrator provisioning. Successful enrollment still requires the future reviewed password-plus-MFA login flow before platform administration.

The owner approved these concrete choices before code/schema implementation. See [mandatory MFA policy](../decisions/0008-platform-administrator-mfa.md) and [existing controls](../decisions/0035-mfa-attempt-replay-controls.md). This internal confirmation workflow does not complete public authentication or the future single-use recovery workflow.
