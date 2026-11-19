# 0040: Atomic Platform Administrator session start and lifetimes

Status: Accepted, 2026-11-18.

The owner approved the [session-start proposal](../architecture/platform-session-start-proposal.md), its lifetime values, and its initial-session behavior. This adds the internal transaction owner across actual MFA acceptance and initial session/refresh persistence. Public login, access-token signing/validation, authorization, and refresh/revocation workflows remain separate work.

## Accepted policy

- Platform access tokens have a five-minute maximum lifetime, capped by the remaining absolute session lifetime when the later JWT issuer is implemented. The current component issues no access JWT.
- Initial refresh tokens expire thirty minutes after session creation. Later reviewed rotation must cap each replacement at the earlier of rotation time plus thirty minutes and the original family expiry.
- Absolute session lifetime is eight hours from creation after successful password-bound MFA. Refresh activity must not extend it. At an expired refresh/absolute deadline, a fresh session requires password plus TOTP under the existing challenge/replay policy.
- These are Platform Administrator rules, not organization-user policy. Refresh inactivity measures token use, not human interaction; background refresh can keep the session alive until the absolute deadline. A separate human-inactivity mechanism is not selected here.
- Each fresh successful sign-in creates an independent family, leaves other families unchanged, and adds no account-wide count limit. Existing TOTP replay protection still prevents creating another session from an already accepted step.

## Implementation boundary

Identity Infrastructure owns PlatformSessionPolicy, IPlatformSessionStarter, and the non-record read-only PlatformSessionStartResult/PlatformSessionTicket. ControlPlane Infrastructure implements PlatformSessionStarter. No schema, package, project, Domain/Application dependency, host DI, API route, or client change is introduced.

StartAsync accepts only the opaque Verification challenge, submitted code, and cancellation token. It rejects an ambient transaction, opens its own fresh private global context with inherited credential diagnostics disabled, and owns a ReadCommitted transaction. Use the actual PlatformMfaVerifier and its account-first/challenge-second locks; never accept a client-selected subject/tenant, stamp, matched step, expiry, or success flag.

Commit denied verification outcomes to preserve legitimate code-failure/replay counters and threshold blocking/revocation. On valid proof, recheck current account eligibility and the consumed challenge's exact binary-collated stamp binding. Late denial rolls back provisional acceptance instead of adopting changed account/claim state. Read issuance time from server UTC after verification and the account recheck.

Generate thirty-two random bytes using .NET cryptography, encode a forty-three-character unpadded base64url token, and store only its uppercase SHA-256 digest. Clear mutable random/hash buffers. Insert the family and initial token in the same transaction as nonce consumption and accepted-step persistence. Build/validate the delivery object before commit and return it only after commit succeeds.

The ticket contains public account/session GUIDs, raw refresh token, UTC creation time, refresh expiry, and absolute expiry. No internal integer ID, stamp, tenant/database target, role grant, authenticated principal, or placeholder access token is returned. Default string representations print no payload fields; do not destructure/log/trace the ticket. Result constructors are not client authentication proof.

SQL/update errors are sanitized without provider details or inner exceptions. Pre-commit failures/cancellation roll back session and MFA changes together. Commit/delivery uncertainty must not cause transparent retries or raw-token redisplay; an acknowledgment failure cannot prove whether commit succeeded. The later client delivery workflow must use a fresh sign-in path when necessary. The current implementation has no automatic retry loop.

Storage expiry dates alone do not enforce authorization or redeem refresh tokens. JWT signing keys/claims, validation and current-session authorization, rotation/reuse concurrency, logout/security-change revocation, cookies/CSRF, public routes/throttling, and recovery authentication remain separately reviewed. The Data Protection PFX is not implicitly a JWT signing key.

See [implementation and verification evidence](../development/platform-session-start.md).
