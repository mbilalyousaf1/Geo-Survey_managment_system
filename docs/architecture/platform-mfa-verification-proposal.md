# Confirmed-factor verification for Platform Administrator sign-in

Status: Accepted and implemented internal verifier, 2026-11-18. The owner approved this slice. See [ADR 0038](../decisions/0038-platform-mfa-verification.md) and [implementation evidence](../development/platform-mfa-verification.md). Session/token policy remains separately reviewed work.

## Behavior

Add an internal verifier accepting only the opaque Verification challenge token and submitted authenticator code. Resolve the account PublicId from trusted stored challenge data; do not accept a client-selected account, tenant, secret, matched step, or success Boolean.

Require an existing caller-owned read-committed global SQL transaction. Resolve the token's stored account association, then acquire account-first/challenge-second locks and revalidate the Verification purpose, lifetime, account/security stamp, administrator claim, password/MFA lockouts, enabled MFA flag, confirmed credential, and issued recovery data. Active password-lockout dates are respected even on legacy rows with the lockout flag disabled. Consumed backup rows remain issued data, consistent with password entry. Enrollment challenges cannot be used for sign-in verification.

Decrypt the confirmed account-bound secret and use TotpAuthenticator to verify the actual code. Wrong/malformed codes under an eligible challenge invoke the existing persistent failure operation. Replayed steps invoke the existing rejection logic. The caller must commit legitimate failure increments, including any block/revocation, rather than rolling them back as exceptions. Invalid challenges or inconsistent credential state must not charge another account or repair/adopt stored factors. Unreadable/noncanonical secret material fails closed as an infrastructure error.

On valid proof, use the existing control store to atomically persist the accepted step, reset only MFA failures, and consume the challenge. Return a server-only result identifying the verified account by public GUID; return no integer ID, security stamp, secret, recovery codes, or token/session artifact. Repeated/concurrent requests must not produce two successful consumptions.

## Transaction/session boundary

The caller owns the transaction so a later reviewed session issuer can persist the session/refresh-token family and commit it together with MFA acceptance. A verified result is provisional until that transaction commits, must be discarded on rollback, must never be deserialized from client input, and is not itself a bearer credential or authenticated session. Calling verification without the required transaction must not succeed. Roll back infrastructure/cancellation errors.

JWT/session lifetimes, signing keys, refresh rotation/reuse handling, cookie/CSRF rules, logout/revocation, recovery-code authentication, public routes/throttling, UI, and host registration remain separately reviewed work. The master specification already requires short-lived access tokens, refresh rotation/reuse detection, safe client storage, and HTTPS; this proposal does not silently select their concrete values or deployment choices.

## Components and verification

- Identity Infrastructure/SignIn: narrow verifier contract and non-secret-printing result.
- ControlPlane Infrastructure/SignIn: transaction-participating implementation using the existing global context, protector, TOTP primitive, and MFA controls. Keep any helper reuse within the current assembly.
- Unit/SQL tests: real password-entry-to-MFA flow with a confirmed factor; wrong/malformed codes; enrollment-purpose rejection; role/scope/account/stamp/expiry binding; inconsistent/unreadable credentials; lockout/blocking; replay; one-winner concurrency; rollback restoring nonce/replay/failure state; required transaction; and no session/credential disclosure.
- Documentation updates after approval.

No new package, schema migration, token issuer, public endpoint, or production account is proposed. See [password entry](../decisions/0037-platform-password-entry.md), [attempt/replay controls](../decisions/0035-mfa-attempt-replay-controls.md), and [master security requirements](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md).
