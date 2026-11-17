# Bootstrap password policy and hashing proposal

Status: Base policy and hashing settings accepted in [record 0023](../decisions/0023-bootstrap-base-password-policy.md), 2026-11-16. Blocklist source/operations and concrete code implementation remain open. The proposal filename is retained to preserve existing links.

Prepared: 2026-11-16.

Implementation update: the separately approved [base normalization and hashing components](../development/bootstrap-password-components.md) now exist with 27 new unit-test cases. They are not registered in the API or used for real account creation. Blocklist operations and complete bootstrap integration remain unresolved/unimplemented; the implementation scope originally proposed below is historical.

Scope: the first Platform Administrator's password. This does not choose organization-user password rules or implement a bootstrap command, login, or credential change workflow.

## Accepted base policy

| Choice | Accepted setting / open item |
| --- | --- |
| Length | 15–128 Unicode code points after NFC normalization |
| Input | Accept valid Unicode and spaces; preserve whitespace; reject invalid Unicode |
| Canonicalization | NFC before length validation and hashing; future verification must apply the same normalization |
| Composition | No mandatory uppercase/lowercase/digit/symbol mix |
| Transformation | No trimming, case folding, truncation, or silently discarded input |
| Periodic rotation | No scheduled expiry; compromise-response and password changes require a separately designed workflow |
| Common/compromised values | Require a blocklist check before real bootstrap creation; source, licensing, updates, and failure behavior remain open |

This proposal is informed by [NIST password guidance](https://pages.nist.gov/800-63-4/sp800-63b.html), but is not a claim of NIST compliance. NIST permits an eight-character minimum for passwords used only within MFA; fifteen is our proposed stronger minimum for this privileged bootstrap account. Code points are not UTF-16 string length or user-perceived grapheme clusters. Password-manager-generated passphrases are encouraged without adding a composition rule.

## Accepted hashing approach

Use the existing ASP.NET Core Identity `IPasswordHasher<IdentityAccount>` / `PasswordHasher<IdentityAccount>`, Identity V3 format, with an explicit 220000 iteration count. In the pinned Identity 10.0.9 implementation this format uses PBKDF2-HMAC-SHA512, a random 128-bit salt, and a 256-bit subkey; parameters are encoded in the hash. See the [framework source](https://github.com/dotnet/aspnetcore/blob/v10.0.9/src/Identity/Extensions.Core/src/PasswordHasher.cs). The work factor follows [OWASP's PBKDF2-SHA512 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2); deployment hardware timing still needs measurement. We retain the already-approved Identity hashing approach rather than introducing another algorithm package.

Store only Identity's encoded hash in PasswordHash. Leave salt generation, derivation, and hash parsing to the framework; do not invent a crypto format. This uses the existing Identity dependency and requires no package or schema change. Hashing does not validate password strength, so validation and hashing must remain distinct steps.

Validate/normalize and hash before acquiring the bootstrap application lock; do not hold that lock during interactive entry or expensive derivation. The deciding administrator-existence check and atomic account/claim writes remain inside the approved transaction/lock. Reruns still cannot overwrite credentials. MFA enrollment remains mandatory before administration.

Future verification must use the same normalization and hashing configuration, recognize failed verification, and correctly handle SuccessRehashNeeded. A rehash indication does not authorize a bootstrap rerun to reset or update an account. Login-time update/concurrency behavior is later implementation work.

## Concrete implementation scope still requiring code approval

- Identity Infrastructure: narrowly scoped normalization/validation code and shared hashing options, using existing packages.
- Unit tests: normalized length boundaries, Unicode/spaces, invalid input without echo, correct/wrong-password verification, distinct salted hashes for identical inputs, and rehash indication for older work factors.
- Document measured hashing cost without logging passwords or hashes.
- No command, API endpoint, account writes, or migration in the base component slice.

The blocklist must have a concrete approved source and operational behavior before claiming the complete bootstrap password policy or enabling real account creation. Do not silently omit it or substitute a placeholder successful check. Operator authorization and command packaging remain separate decisions.

## Related decisions

- [Bootstrap command principles](../decisions/0007-first-platform-administrator-bootstrap.md)
- [Mandatory MFA](../decisions/0008-platform-administrator-mfa.md)
- [Administrator claim](../decisions/0021-platform-administrator-claim.md)
- [Bootstrap transaction/lock](../decisions/0022-bootstrap-transaction-and-lock.md)
