# 0023: Bootstrap base password policy and hashing

Status: Accepted

Accepted and recorded: 2026-11-16

## Decision

The product owner approved these settings for the first Platform Administrator:

- Accept valid Unicode and spaces. Normalize to NFC before counting and hashing; preserve whitespace without trimming, case folding, or truncation.
- Require 15–128 Unicode code points after normalization. Do not count UTF-16 code units or require uppercase/digit/symbol mixtures.
- Do not impose scheduled password expiry. Compromise response and credential changes require a separately designed workflow.
- Use the existing Identity V3 `IPasswordHasher<IdentityAccount>` / `PasswordHasher<IdentityAccount>` implementation with 220000 iterations. The pinned implementation uses PBKDF2-HMAC-SHA512 with a random salt and encoded parameters; do not introduce custom cryptography or another package.
- Future verification must use the same NFC normalization and shared hashing configuration. Handle verification failure and rehash indication correctly in its reviewed workflow; bootstrap reruns cannot update credentials.

Validation/normalization and expensive hashing precede the bootstrap lock. The deciding existence check and atomic account/claim persistence stay within the accepted transaction and application lock. Mandatory MFA remains unchanged.

## Boundaries and open work

This is the accepted base policy, not a complete password-security implementation or an organization-user policy. A common/compromised-password blocklist must have an approved source and operational behavior before real bootstrap account creation; source, licensing, updates, and failure policy remain open. Deployment hashing cost requires measurement.

This documentation update implements no code, registration, account write, command, migration, or database action. A small normalization/base-validation and shared-hashing component can be planned separately, with relevant unit tests, before code approval. It must not claim to implement the unresolved blocklist.

Operator authorization, command packaging, MFA enrollment/session handling, recovery, and disabled/deleted administrator behavior remain separate decisions.

## Related documentation

- [Password policy design](../architecture/bootstrap-password-policy-proposal.md)
- [Bootstrap transaction and lock](0022-bootstrap-transaction-and-lock.md)
- [First-administrator bootstrap](0007-first-platform-administrator-bootstrap.md)
- [Decision index](README.md)
