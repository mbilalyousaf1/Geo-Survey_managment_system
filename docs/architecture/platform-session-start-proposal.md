# Platform Administrator session start and lifetime proposal

Status: Accepted, 2026-11-18. The owner approved the lifetime values, initial-session behavior, and internal implementation. See [ADR 0040](../decisions/0040-platform-session-start-and-lifetimes.md) and [implementation evidence](../development/platform-session-start.md). Public authentication/authorization and refresh redemption remain separate work.

## Purpose

Connect the existing [confirmed-factor verifier](../decisions/0038-platform-mfa-verification.md) to the [session/refresh storage](../decisions/0039-session-refresh-token-storage.md). The verifier currently returns a provisional server-side subject inside a caller-owned transaction; the next small implementation will own that transaction, persist a real token family and initial refresh digest, and deliver its raw refresh token only after commit.

This is an internal session-start component. Its success means those records were committed. It does not yet deliver a signed access token, create an authenticated HTTP principal, or complete public login/authorization. No placeholder JWT or successful public authentication response is proposed.

## Accepted lifetime values

These values apply only to Platform Administrator sessions; organization-user policy is not selected here.

| Control | Accepted value | Meaning |
| --- | --- | --- |
| Access-token maximum lifetime | 5 minutes | Policy for the later JWT issuer; cap expiry at the session's absolute expiry. No access token is minted by this slice. |
| Refresh inactivity window | 30 minutes | Initial refresh token expires 30 minutes after session creation. A later reviewed successful rotation would set its replacement expiry to the earlier of rotation time plus 30 minutes and the original family expiry. |
| Absolute session lifetime | 8 hours | Set once at session creation after successful password-bound MFA verification. Refresh activity must never move this deadline. |

All expiry checks use server UTC; a deadline is expired when now is equal to or after it. A refresh token that reaches its deadline cannot renew the family. A new session then requires fresh password entry and TOTP under the existing challenge/replay policy. Reaching the absolute limit requires fresh password plus TOTP even if refresh traffic continued.

The refresh window measures use of a refresh token, not human interaction. Background refresh can keep it alive until the absolute deadline. This is not a promise of automatic logout after 30 minutes away from the keyboard, and it does not select a separate browser/human-inactivity mechanism. Public API/client behavior and enforcement of the deadlines are implemented in their later reviewed flows; storing dates alone is not enforcement.

Each successful fresh sign-in creates an independent family. Starting it does not revoke other existing families or impose an account-wide session count limit. The owner approved this initial behavior; later logout, administrative revocation, and security-change policies remain open. Existing TOTP replay rules still prevent using one accepted step to create multiple sessions.

The numeric values are accepted GeoSurvey policy, not values mandated by the cited standards. RFC 9700 describes refresh inactivity expiry with server-chosen durations. OWASP distinguishes inactivity and absolute limits and ties their selection to application risk and use. See the references below.

## Internal contract and transaction

Add IPlatformSessionStarter.StartAsync(verificationToken, code, cancellationToken). The inputs contain only the existing opaque Verification challenge and submitted TOTP code, never a client-selected account/family, tenant, security stamp, matched step, expiry, or MFA-success Boolean.

PlatformSessionStarter will use a fresh private global context, with credential diagnostics suppressed as in the existing password-entry service. Reject an ambient transaction and own a ReadCommitted transaction; do not return a ticket whose commit depends on an outside caller.

1. Call the actual PlatformMfaVerifier with that context, protector, and server clock. Reuse its token-resolved account binding, current eligibility checks, account/challenge locks, confirmed TOTP, failure budget, replay controls, and conditional nonce consumption.
2. If verification denies, commit legitimate failure/block updates and return an empty rejection. Invalid challenges do not charge another account. Do not turn normal wrong-code denials into rollback exceptions.
3. On success, resolve the internal account ID and current stamp only from the verified public subject in that transaction. Preserve the existing eligibility/stamp boundary and locking order; do not accept a supplied proof/result object or account identifier from the client.
4. Read issuance time from the UTC server clock after verification. Generate 32 cryptographically random bytes, encode the opaque token as unpadded base64url, and compute the uppercase SHA-256 digest of its ASCII representation. Persist no raw token. Clear mutable random/hash buffers where applicable.
5. Insert TokenFamily and its initial RefreshToken with the approved expiry calculation in the same transaction as MFA acceptance. The existing unique/FK/UTC constraints remain authoritative storage guards.
6. Commit, then return a non-record, read-only PlatformSessionStartResult containing a ticket only on committed creation. The ticket includes public account/session GUIDs, raw refresh token, creation time, refresh expiry, and absolute session expiry. It contains no internal integer ID, stamp, tenant/database reference, role grant, or placeholder access token. Its default string representation must not print payload fields; do not log/destructure/trace the ticket.

Infrastructure, cryptographic, or pre-commit cancellation errors roll back the new session rows and MFA state. Sanitize SQL/update errors without provider details or inner exceptions. Commit/delivery uncertainty must not trigger transparent session creation retries: never redisplay a raw token from its hash, and never issue another family by treating a consumed challenge as new proof. A later caller must handle lost delivery through a fresh reviewed sign-in path.

The component only starts a new family. It does not rotate or redeem refresh tokens, revoke existing access JWTs, register API routes/DI, choose cookies, modify account/MFA credentials, or grant tenant access. Signing keys/claims and request authorization require their own implementation review before public login is complete. The existing Data Protection certificate is not implicitly reused for JWT signing.

## Approved implementation plan

| Project | Planned files and change |
| --- | --- |
| Identity Infrastructure | Sessions/PlatformSessionPolicy.cs for the accepted durations; SignIn/IPlatformSessionStarter.cs for the narrow contract and non-record result/ticket |
| ControlPlane Infrastructure | SignIn/PlatformSessionStarter.cs to own the private context, real verification, digest generation, persistence, and commit |
| Identity unit tests | PlatformSessionStartResultTests.cs for rejection shape, read-only results, and secret-free default strings |
| ControlPlane SQL integration tests | PlatformSessionStarterTests.cs for the actual password/enrollment/verification-to-session flow and its denial/rollback/concurrency boundaries |
| Documentation | ADR, implementation evidence, master accepted-decision note, README/repository structure, and Phase 1 progress after approval |

No schema migration, new package/project, Domain dependency, public API, or client change is needed. Keep the existing verifier contract and Identity-only model intact; do not refactor unrelated authentication components.

## Required verification

- Real password-entry challenge plus confirmed TOTP creates exactly one account-bound family and token digest; the returned raw token hashes to the stored value and is not stored as plaintext.
- Server-derived IDs, stamp snapshot, UTC timestamps, and exact approved expiry values; issue time is read after lock waits/verification, not supplied by the client.
- Wrong/malformed codes commit the existing counters and fifth-failure block; rejected/expired/replaced/consumed/enrollment challenges and ineligible scope/claim/lockout/stamp/factor states create no session.
- TOTP replay and simultaneous completion leave one successful family/token creation for a challenge; fresh sign-in follows the existing replay constraints and leaves other families unchanged.
- Real SQL failure after session writes rolls back family/token, nonce consumption, accepted step, and verification controls. Cancellation and decryption failures do not return a ticket. Unsafe logging/ambient transaction configuration fails safely.
- Retried consumed challenges deliver no token; a committed success is returned only after commit, with no secret-bearing ToString or internal identifiers. No test substitutes a fake JWT/authentication implementation.

Run the Release build, targeted unit tests, disposable Docker SQL backend suite, formatting/analyzers, and diff checks; report exact commands and outcomes. Current results belong in the [implementation evidence](../development/platform-session-start.md). Proposal-time documentation checks and the earlier 541-test storage result are not evidence of this implementation's application verification.

## References

- [Master token security baseline](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md#93-authentication-and-token-security)
- [Phase 1 completion plan](../development/phase-1-completion-plan.md)
- [RFC 9700 section 4.14.2](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2)
- [OWASP session expiration guidance](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html#session-expiration)
