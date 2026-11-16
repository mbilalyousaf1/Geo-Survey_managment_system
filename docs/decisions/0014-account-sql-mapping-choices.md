# 0014: Account SQL mapping choices

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

The product owner reviewed the [account EF mapping design](../architecture/identity-account-ef-mapping-design.md) and approved the specific choices below. The single-account-table and scoped-uniqueness strategy is already accepted in [record 0011](0011-scoped-single-account-table.md).

## Decision

| Mapping choice | Accepted behavior |
| --- | --- |
| Table | `identity.Accounts` in the global control-plane database |
| Scope encoding | Required integer: Platform = 1, Organization = 2 |
| Email fields | Required `nvarchar(254)` for Email and NormalizedEmail |
| Internal username fields | Required `nvarchar(32)` for UserName and NormalizedUserName |
| Normalized comparisons | Column-level `Latin1_General_100_BIN2` for NormalizedEmail and NormalizedUserName |
| Scoped email uniqueness | Separate filtered unique indexes: organization scope on `(OrganizationId, NormalizedEmail)`; platform scope on NormalizedEmail |
| Organization FK deletion | `NO ACTION`, preventing cascading account deletion |

Database-generated integer account IDs, application-generated PublicId, unique public identifiers/internal usernames, and valid scope/organization combinations remain requirements from earlier accepted records. No unfiltered globally unique email index is approved.

## Consequences

The application must normalize before storage and lookup. Binary collation does not perform case normalization, and SQL Server padding semantics mean it is not a complete database email validator. Continue enforcing the approved `AccountEmail` contract; verify actual SQL Server behavior with integration tests.

NO ACTION protects dependent account records; it does not define a deletion/retention workflow or whether retired identities/codes can be reused. Cross-module database referential integrity does not permit Identity to query or manipulate Organizations' private tables or DbContext at runtime.

The target organization table/key contract and global DbContext/migration ownership remain open. Do not create placeholder organization persistence or omit the intended FK silently. Exact migration/index/check names, remaining Identity fields, and credential/MFA/session mappings still need review. This record accepts only the listed choices, not every example or implementation suggestion in the mapping design.

No EF Core package, DbContext, mapping class, migration, SQL execution, or database change is introduced by this documentation update.

## Required later verification

Use real SQL Server tests for scope checks, FK existence/deletion behavior, scoped concurrent uniqueness, lengths/nullability, and normalized-column comparison. Existing object/architecture tests do not verify these unimplemented mappings.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Mapping design and test scenarios](../architecture/identity-account-ef-mapping-design.md)
- [Decision index](README.md)
