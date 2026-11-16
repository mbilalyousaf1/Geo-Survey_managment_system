# 0017: Organization mapping and materialization

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

The product owner approved the Organization mapping and materialization choices in the [control-plane design](../architecture/organization-mapping-and-control-plane-design.md). Earlier records accept the table/key contract and minimal entity; this record captures the additional mapping and constructor choices. Exact context/project structure remains a separate review.

## Decision

| Property | Accepted mapping |
| --- | --- |
| Table | `organization.Organizations` |
| `Id` | Database-generated integer primary key |
| `PublicId` | Required `uniqueidentifier`, unique index, application-generated with no database default |
| `Code` | Required `nvarchar(50)`, unique index, column collation `Latin1_General_100_BIN2` |
| `DisplayName` | Required `nvarchar(200)`, not unique |
| `CreatedAtUtc` | Required `datetimeoffset(7)`, application-assigned UTC, no database default |

Map Code as one scalar column using a value converter: write `OrganizationCode.Value`, read through `OrganizationCode.Create(storedValue)`. Domain stays free of EF Core dependencies. Verify record equality/change tracking and query translation; do not add a custom comparer without evidence of need.

Use a private materialization constructor accepting the mapped properties (`id`, `publicId`, `code`, `displayName`, `createdAtUtc`) without generating identity or time. Keep the public Create factory as the ordinary creation path. Refactor its private construction path as needed so EF binding is unambiguous. Do not add a public invalid-state constructor or EF annotations to Domain.

## Consequences and limits

Application code must persist normalized Code values. Binary collation alone is not domain validation, and conversion on read can normalize stored text; verify raw stored values as well as materialized objects. Required columns/indexes enforce storage shape and uniqueness, not historical immutability or authorization.

Fresh-read SQL Server integration tests must preserve the original database ID, PublicId, Code, display name, creation timestamp, and UTC offset without generating replacement values. Also test concurrent duplicate rejection, allowed duplicate display names, converter tracking, and queries.

Index/check naming, additional direct-SQL canonical-value guards, a UTC-offset CHECK constraint, context composition/project names, migration-history location, and runtime persistence adapters are not approved by this record. No entity refactor, EF package, mapping, context, migration, or database change is implemented by this documentation update. Implementation still needs a concrete approved task.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Persistence ownership](0015-control-plane-persistence-ownership.md)
- [Minimal Organization entity](0016-minimal-organization-entity.md)
- [Decision index](README.md)
