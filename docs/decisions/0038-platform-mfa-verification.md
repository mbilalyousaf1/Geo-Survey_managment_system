# 0038: Confirmed-factor verification for Platform Administrator sign-in

Status: Accepted, 2026-11-18.

The owner approved the [confirmed-factor verification proposal](../architecture/platform-mfa-verification-proposal.md). This adds the internal password-challenge-to-confirmed-TOTP verification step without issuing sessions or choosing token lifetimes, signing keys, refresh behavior, cookies, public endpoints, or recovery authentication.

Identity Infrastructure owns IPlatformMfaVerifier and the non-record PlatformMfaVerificationResult. ControlPlane Infrastructure implements PlatformMfaVerifier using the existing global context, account-bound protector, TOTP primitive, and attempt/replay stores. No dependency, schema, or migration is introduced.

The only verification inputs are the opaque challenge token, submitted code, and cancellation token. Resolve the account PublicId from trusted token-hash/purpose storage. Treat that first lookup as routing only. Under account-first/challenge-second update locks, revalidate the current Verification purpose, expiry, stamp, Platform scope/administrator claim, enabled MFA, password/MFA lockouts, confirmed credential, and issued recovery rows. Active password-lockout dates reject even when a legacy LockoutEnabled flag is false. Exhausted backups do not prevent TOTP verification. Missing/inconsistent factors are not adopted or repaired.

Use the existing account-bound decryption and real TOTP calculation. Wrong/malformed codes use persistent failure recording; reused/older steps use persistent replay rejection. Eligible denied outcomes require the caller to commit their failure increments. Invalid challenges/state cannot charge another account. Unreadable or noncanonical confirmed material fails closed with no code-failure charge. SQL/update exceptions are replaced by a fixed error without provider diagnostics/inner exceptions; the caller must roll back infrastructure errors.

Require the caller's existing ReadCommitted transaction. Higher isolation modes are rejected because the preliminary routing read must release read locks before the existing account-first locking sequence. On valid proof, consume the challenge and save/reset MFA controls inside that transaction. Return only a provisional public account GUID; never return integer IDs, stamps, secrets, recovery codes, access tokens, or a session. Discard the result on rollback. The result is a trusted internal return value, not a bearer credential or something to deserialize from client input.

The future session coordinator must persist session/refresh state and commit together with successful MFA acceptance. This verifier never commits, rolls back, creates a principal, or grants platform permissions itself. Direct low-level nonce consumption or a Boolean from a client is not a replacement for this trusted flow. No host DI/public route is registered in this slice.

See [implementation and test evidence](../development/platform-mfa-verification.md).
