# 0035: Persistent Platform Administrator MFA attempt/replay controls

Status: Accepted, 2026-11-17.

The owner approved persistent controls and selected five failed codes within 15 minutes causing a 15-minute MFA block. The block starts at the fifth failure, is shared across enrollment/verification challenges for the account, and is independent of password lockout. Do not apply this decision to organization-user or recovery-code limits implicitly.

Add Identity-owned MfaVerificationState, mapping, policy constants, a narrow store contract, and MfaVerificationService. ControlPlane Infrastructure implements SQL persistence using account-first, then challenge base-row locks. The fifth migration creates identity.MfaVerificationStates, keyed by AccountId with a NO ACTION account FK, bounded failure count, UTC failure-window/block timestamps, and nullable nonnegative LastAcceptedStep. It stores no raw codes, tokens, or secrets and seeds no state.

Only a currently valid account/purpose/stamp-bound challenge may affect state. Count malformed/incorrect codes through the trusted workflow's failure operation. Replacement challenges, purpose changes, process-local state loss, password/security-stamp changes, and pending-secret replacement do not clear the account budget. Requests with invalid challenges or during a block do not extend it. Window expiry below threshold or block expiry starts a new failure budget on the next valid attempt; success resets failures/window/block. Replay history persists through these resets.

At threshold, atomically persist the block and revoke all outstanding account MFA challenges. Challenge issuance, validity, consumption, and pending access share the active-block eligibility predicate. At block expiry the old challenges remain revoked; obtain a fresh password-verified challenge.

Accept only a trusted verifier's matched step in the current +/- one-step window, strictly greater than LastAcceptedStep. Replayed/older/out-of-window steps under a valid challenge count as failures. Successful acceptance requires a caller-owned transaction, consumes the challenge conditionally, and persists/reset controls in that same transaction. A consumed/otherwise-invalid challenge changes no counters. Accepted-step success is nonce/replay-state acceptance, not an authentication grant or enrollment confirmation.

The future caller must commit rejected valid-challenge failures to preserve the budget, and must roll back infrastructure errors. It must commit confirmed/authenticated state together with successful control changes. No standalone success transaction, public endpoint, MFA enablement, confirmed secret, recovery code, password verification, access token, or host DI wiring is introduced here. Direct low-level challenge consumption alone does not provide these replay guarantees and must not replace this completion control path.

See [runbook and verification](../development/mfa-attempt-replay-controls.md).
