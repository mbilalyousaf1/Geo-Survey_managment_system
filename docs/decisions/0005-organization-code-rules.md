# 0005: Organization-code rules

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

[Record 0004](0004-organization-code-login.md) selects organization code + email + password for web and mobile login. Consistent code validation and lookup rules are needed to distinguish organizations. The product owner approved the rules below.

## Decision

| Aspect | Approved rule |
| --- | --- |
| Selection | Platform Administrator chooses the code when creating the organization |
| Normalization | Trim surrounding spaces and convert uppercase letters to lowercase |
| Length | 3-50 characters after normalization |
| Characters | Lowercase English letters `a-z`, digits `0-9`, and hyphens |
| Hyphens | No leading, trailing, or consecutive hyphens |
| Uniqueness | Unique across the platform, using the normalized code |
| Changes | Fixed after organization creation |

For example, ` ACME-Surveys ` becomes `acme-surveys`. Embedded spaces, underscores, and non-English letters are invalid; normalization does not replace invalid characters or collapse repeated hyphens. The display name remains separate from the login code.

## Consequences

- Organization creation and login must use the same normalization and validation behavior. Case differences and surrounding spaces do not identify different organizations.
- Server-side validation is authoritative. A later persistence implementation must enforce normalized uniqueness to prevent concurrent duplicate creation; this record does not select a database schema or collation.
- A fixed code provides a stable login identifier even when the display name changes. No code-renaming workflow is approved.
- Knowing a valid code does not authenticate a user or authorize tenant access. The independent-account and trusted registry rules remain unchanged.
- This decision was initially recorded without implementation. The subsequently approved `OrganizationCode` Domain value object now implements normalization and format validation, with unit tests. No code generator, lookup endpoint, database migration, or UI has been implemented.

## Remaining details

Reserved-code lists and whether codes can be reused after organization retirement/deletion are not specified. Do not infer a reuse or deletion policy from the uniqueness rule. Exact API errors, implementation details for normalization, and database mapping remain future design work.

## Related documentation

- [Master specification, section 6.1](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Separate organization accounts](0003-separate-organization-accounts.md)
- [Decision index](README.md)
