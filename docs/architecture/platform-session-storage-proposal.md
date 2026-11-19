# Platform Administrator session and refresh-token storage proposal

Status: Accepted, 2026-11-18. The owner approved this storage-only implementation. See [ADR 0039](../decisions/0039-session-refresh-token-storage.md) and [implementation evidence](../development/session-refresh-token-storage.md). This decision approves no session lifetime, signing key, API, or authentication behavior.

## Purpose and concept

The [confirmed-factor verifier](../decisions/0038-platform-mfa-verification.md) verifies password-challenge-bound TOTP inside a caller-owned transaction. Its returned public account identifier remains provisional. The eventual sign-in coordinator must save the session and initial refresh-token state in that same transaction before returning credentials.

A token family represents one sign-in session. Rotation replaces refresh token A with B, then B with C, while all three remain associated with that family. Keeping consumed token digests allows a later refresh workflow to recognize reuse and revoke the family. A new sign-in creates a separate family; matching account emails never join sessions across account scopes or organizations.

The master specification already requires refresh rotation, reuse detection, and secure storage. This proposal adds only the persistent records and database constraints needed by those later workflows. Storage alone cannot authenticate an account or grant Platform Administrator permissions.

## Approved records

Keep these persistence models and EF configurations in Identity Infrastructure, with their mappings composed only into the global control-plane context, following the existing MFA storage pattern. Do not add an Application dependency on Infrastructure or introduce a new project/package.

| Record | Stored fields | Purpose |
| --- | --- | --- |
| TokenFamily | Internal long Id; unique public GUID PublicId; AccountId; SecurityStamp; CreatedAtUtc; ExpiresAtUtc; nullable RevokedAtUtc | Account-bound session identity, credential-version snapshot, absolute expiry, and family-wide revocation |
| RefreshToken | Internal long Id; TokenFamilyId; unique TokenHash; CreatedAtUtc; ExpiresAtUtc; nullable ConsumedAtUtc | Digest lookup, per-token expiry, and retained evidence that a token was used |

Use identity.TokenFamilies and identity.RefreshTokens in the global database. No tenant database reference, organization selector, email, IP address, device fingerprint, raw token, JWT, authenticator secret, or password is stored in these records. AccountId remains internal; PublicId is not a credential or authorization proof.

TokenHash uses the existing SHA-256 digest representation: canonical 64-character uppercase hexadecimal text in a binary-collated char(64) column. A later reviewed issuer will generate high-entropy opaque tokens and calculate their digests; this slice adds no raw-token generator or credential-returning result. A fast hash is appropriate for random bearer secrets, unlike human passwords, which retain the existing slow password-hashing policy.

SecurityStamp captures the account credential version for later current-state checks. A foreign key and stored stamp do not enforce current scope, administrator status, MFA completion, lockout, or authorization: the future coordinator and validators must perform those checks against trusted account state.

## Approved database integrity

- Account-to-family and family-to-token foreign keys use NO ACTION. Do not silently cascade-delete session/reuse evidence or choose a retention/deletion policy.
- Unique indexes protect family PublicId and token digest. Index family AccountId for account-bound session lookup.
- A filtered unique index on TokenFamilyId where ConsumedAtUtc is null permits at most one unconsumed token per family. An expired unconsumed token still occupies that slot; expiry must never be treated as permission to insert a replacement without the reviewed transition. Concurrent sessions remain distinct families, with no account-wide session limit selected here.
- Require nonempty family public identifiers and security stamps, positive expiry intervals, and revocation/consumption timestamps no earlier than their record creation. Store timestamps as datetimeoffset(7) and enforce zero UTC offsets.
- Enforce canonical digest shape and required fields. Parent-row expiry, account eligibility, immutable account/stamp binding, token consumption, and family revocation still need transactional workflow enforcement; an EF mapping is not that enforcement.

Retain consumed rows so later reuse detection can identify the affected family. Do not add rotation, logout, cleanup jobs, retention intervals, or an administrative session-management API in this slice.

## Approved implementation plan

1. Add TokenFamily.cs, TokenFamilyConfiguration.cs, RefreshToken.cs, and RefreshTokenConfiguration.cs under src/Backend/Modules/Identity/GeoSurvey.Identity.Infrastructure/Sessions/.
2. Apply those configurations in src/Backend/GeoSurvey.ControlPlane.Infrastructure/GlobalControlPlaneDbContext.cs. Keep the separate Identity-only context model unchanged.
3. Generate and review AddTokenFamiliesAndRefreshTokens plus its designer/snapshot and a forward SQL review artifact. Execute migrations only against disposable test databases.
4. Add real-SQL mapping/constraint and existing-database upgrade tests. Adjust existing global table/FK/model expectations only where the two new mappings require it.
5. Update the ADR, repository structure, master implementation notes, and development evidence after implementation, keeping later policies explicitly unresolved.

## Verification and completion boundary

Exercise fresh migration, upgrade from AddConfirmedMfaEnrollment with existing account/MFA data preserved, migration reapplication, snapshot consistency, foreign-key/no-cascade behavior, digest/public-ID uniqueness, single unconsumed token per family, multiple families per account, UTC/chronology constraints, and retained consumed token history. Prove that no token plaintext column or unintended Identity-only mapping appears. Storage fixtures are test data, not fake authentication implementations.

Run the Release build, relevant SQL tests through the disposable Docker SQL runner, the backend suite, formatting/analyzers, and diff checks. See the [implementation notes](../development/session-refresh-token-storage.md) for current results; proposal-time documentation checks are not application verification evidence.

Completion means reviewed storage and migration tests, not a working login session. Integration with actual MFA verification, atomic session issuance/refresh, concurrent refresh handling, reuse-driven revocation, and server-side authorization must receive their own small implementation reviews.

## Decisions deliberately still open

- Access-token lifetime, refresh inactivity window, absolute session lifetime, and concurrent-session limits.
- JWT issuer/audience/claims, signing algorithm, separately managed signing keys, rotation, and validation. The existing Data Protection PFX is not implicitly a JWT signing key.
- Exact refresh transaction/locking and replay policy, including concurrent browser requests or retries, and the effect of revocation on already-issued access tokens.
- Logout, password/MFA changes, administrative revocation, retention, and recovery authentication semantics.
- Browser deployment topology, cookie scope/SameSite/CSRF protection, public routes/throttling, and MAUI integration. Refresh tokens must not be put into localStorage.

Store explicit expiry timestamps now; select their actual durations when reviewing the issuer. This permits the storage foundation without silently selecting user-facing session behavior.

## References

- [Master specification: authentication and token security](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md#93-authentication-and-token-security)
- [Current Phase 1 plan](../development/phase-1-completion-plan.md)
- [RFC 9700 section 4.14: refresh-token protection](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14). Its rotation guidance retains token relationships to detect replay; it does not prescribe GeoSurvey's lifetime values or make this application an OAuth authorization server.
