# 0030: Restricted MFA challenges

Status: Accepted, 2026-11-17.

The owner approved a bounded storage/service slice for Platform Administrator MFA enrollment and verification. A trusted issuing workflow must first verify the password and supply the verified account PublicId and security stamp. These are server-side inputs, not client assertions.

Use 32 cryptographically random bytes encoded as an opaque base64url token, storing only its SHA-256 digest. Challenges expire after ten UTC minutes and are bound to account, purpose, and security stamp. Issuance replaces outstanding challenges for the same account/purpose. Successful MFA completion must consume the challenge once; consuming a challenge alone does not authenticate anyone.

Identity Infrastructure owns the model, mapping, lifecycle service, and narrow persistence contract. ControlPlane Infrastructure implements persistence because it owns the composed context. No new package or API endpoint is introduced. An account-row transaction lock serializes replacement; a filtered unique index enforces one outstanding challenge per account/purpose. Conditional SQL consumption permits one winner and can participate in the caller's transaction.

Eligibility requires Platform scope, the administrator claim, an existing security stamp, no current lockout, and the appropriate MFA state. Enrollment requires MFA disabled; verification requires it enabled. Stamp comparisons use a binary collation. Expired/consumed/revoked rows are retained; cleanup policy remains undecided.

This decision does not choose TOTP-secret protection, attempt limits, recovery-code design, JWT/session policy, reset/recovery, or retention. No administrator token may be issued before successful MFA. Those later workflows require separate review.

See [implementation and verification](../development/mfa-challenges.md).
