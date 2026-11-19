# Platform Administrator password verification and restricted MFA entry

Status: Accepted and implemented internal service, 2026-11-18. The owner approved this design and the separate five-consecutive-failure/15-minute password-lockout policy. See [ADR 0037](../decisions/0037-platform-password-entry.md) and [implementation evidence](../development/platform-password-entry.md).

## Next bounded behavior

Add a trusted internal entry service accepting email and password for the dedicated Platform Administrator scope. Look up only the normalized platform email and require the server-managed administrator claim. Matching organization-account emails must not be consulted for credential verification or modified by this flow.

Reuse AccountEmail normalization, IdentityPasswordHashingOptions, and the framework password hasher. Normalize supplied passwords to NFC while preserving whitespace and case. Bound normalized input to the existing maximum of 128 Unicode code points; do not rerun the current password-creation minimum or local blocklist during login. Honor framework rehash-needed outcomes atomically using the approved hashing configuration, with current stamp binding for any issued challenge.

Verify password hashes before acquiring the account update lock, then recheck the hash/security stamp, current role/scope, lockout, and MFA state under the lock before applying changes or issuing a challenge. A changed credential must not authorize a challenge based on stale verification. Unknown/noneligible account failures perform dummy password-hash work and use the same rejection result as wrong passwords or lockout. Do not promise identical end-to-end timing; application/request rate limiting still requires its later API design.

If the password is valid, issue only the existing ten-minute Enrollment challenge when MFA is unconfigured, or Verification challenge when a confirmed authenticator exists. Reject inconsistent flag/credential/recovery state instead of adopting or overwriting it. No access JWT, refresh token, cookie, authenticated principal, organization context, password reset, or public endpoint is introduced.

## Approved password lockout

- Enable password lockout for Platform Administrators using the existing Identity AccessFailedCount, LockoutEnabled, and LockoutEnd fields.
- Five consecutive failed password attempts cause a 15-minute password lockout and revoke outstanding account MFA challenges.
- Attempts during active lockout do not extend it; a correct password does not bypass active lockout.
- At lockout expiry, restart the password-failure budget. Correct password verification clears only this password counter, never MFA attempt/replay state.
- Count failures only for an existing eligible platform administrator; unknown email/organization accounts/nonadministrator accounts cannot have their counters changed by this flow.
- These values are an explicitly approved application policy, not an inferred framework default. It is a consecutive-failure policy, distinct from the MFA fixed 15-minute failure window.

The runtime path must explicitly establish LockoutEnabled for eligible platform administrators, including existing bootstrap-created accounts whose inherited default is false, when applying password-attempt state. No blanket migration, operator grant change, or unrelated organization-account policy is proposed. Existing active lockouts must be respected and must not be cleared opportunistically.

## Files and checks

Identity Infrastructure owns the input-normalization helper/contract and non-secret-printing challenge result. ControlPlane Infrastructure implements the transaction-owning entry workflow using a fresh private context and the existing challenge issuer. Existing fields/packages are reused; no migration or API registration is added. The issuer gained TryIssueAsync for ordinary eligibility denial; IssueAsync retains its prior exception behavior.

Unit/SQL tests must cover actual password hashing, NFC/whitespace behavior, scoped email reuse, generic rejection/digest/token non-disclosure, correct challenge purpose, no administration grant, inconsistent MFA state, MFA-block independence, password-lockout threshold/expiry/no extension, concurrent counter updates, stale hash/stamp denial, rehash, cancellation, and atomic challenge/counter rollback. Public API throttling, confirmed-factor login completion, session policy, recovery-code consumption, and UI remain later review boundaries.
