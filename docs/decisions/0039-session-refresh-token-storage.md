# 0039: Session families and refresh-token digest storage

Status: Accepted, 2026-11-18.

The owner approved the [storage-only proposal](../architecture/platform-session-storage-proposal.md). Add the persistent session/refresh foundation required before atomic sign-in completion; do not choose lifetimes, JWT keys, client transport, or refresh/revocation behavior in this slice.

Identity Infrastructure owns TokenFamily, RefreshToken, and their EF configurations under Sessions. Apply them only in the composed GlobalControlPlaneDbContext. The separate Identity-only model remains four tables. Keep Domain/Application dependencies unchanged; add no project, package, generic repository, or service without a current behavior to implement.

identity.TokenFamilies stores a bigint internal key, unique nonempty GUID PublicId, internal AccountId FK, nonempty SecurityStamp snapshot, creation/absolute-expiry UTC timestamps, and nullable UTC revocation time. identity.RefreshTokens stores a bigint internal key, TokenFamilyId FK, unique canonical uppercase SHA-256 digest, creation/expiry UTC timestamps, and nullable UTC consumption time. No raw bearer token, JWT, tenant database reference, password, authenticator secret, IP address, or device fingerprint is stored.

Both foreign keys use NO ACTION. Index the family account association and public GUID. Enforce one unconsumed token per family with a filtered unique index; consumed rows remain available for later replay recognition. Expired tokens and tokens in revoked families still occupy that slot until a reviewed state transition. This does not select an account-wide concurrent-session limit or a retention/deletion policy.

Require positive expiry intervals, zero stored UTC offsets, and state timestamps at or after creation. Enforce digest shape in SQL as well as factory validation; factories canonicalize valid hexadecimal input to uppercase. Explicit expiry timestamps are supplied by future trusted callers, with no default duration hidden in these models.

A family FK/public identifier/stamp is not proof of authentication. Current account eligibility, scope/administrator claim, MFA, lockout, stamp equality, parent expiry, immutable binding, and authorized consumption/revocation require the future reviewed transactional workflows. Their implementation is not implied by these tables. In particular, the future coordinator must create session state together with successful MFA acceptance in the same transaction, return credentials only after commit, and preserve legitimate failed-attempt counts on denied outcomes.

The seventh migration, 20261118070921_AddTokenFamiliesAndRefreshTokens, only adds these tables, constraints, indexes, and FKs. It alters no existing table or account/MFA data and seeds no session. Global totals become thirteen application tables, fourteen including migration history, and twelve NO ACTION foreign keys. Generate forward SQL offline and test migrations only in disposable databases.

Concrete session/token durations, signing keys/claims, rotation concurrency/reuse handling, revocation of existing access tokens, logout, security-change behavior, cookies/CSRF, public endpoints, recovery authentication, and client integration remain separately reviewed work. The existing Data Protection certificate is not implicitly a JWT signing key.

See [implementation and verification notes](../development/session-refresh-token-storage.md).
