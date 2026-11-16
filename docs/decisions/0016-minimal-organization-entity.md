# 0016: Minimal Organization entity

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

The [control-plane ownership decision](0015-control-plane-persistence-ownership.md) defines Organizations ownership and the `organization.Organizations` table/key contract. A real organization record is needed before the account FK can be implemented. The product owner approved this minimal model without introducing provisioning rules.

## Decision

| Field | Approved rule |
| --- | --- |
| `Id` | Database-generated integer internal key |
| `PublicId` | Application-generated `Guid.NewGuid()`, fixed after creation |
| `Code` | Existing `OrganizationCode` value object, fixed after creation |
| `DisplayName` | Required; trim surrounding whitespace; 1-200 characters after trimming; not unique |
| `CreatedAtUtc` | UTC `DateTimeOffset`, assigned when created |

Use a private constructor and a creation method to establish valid initial state. The entity belongs to Organizations Domain and must not depend on EF Core or Identity. Exact method signature, timestamp sourcing, and exception behavior require an implementation plan.

## Consequences

PublicId/code uniqueness must later be enforced through database constraints, including under concurrent creation. Factories alone cannot establish uniqueness. DisplayName is distinct from the immutable login code; duplicate display names are allowed. No additional display-name character restrictions are approved here.

The minimal entity introduces no rename operation, deletion workflow, provisioning status, region, tenant-database reference, account creation, or automatic readiness behavior. Missing these fields does not imply that an organization is provisioned or ready for operational requests. Those requirements must be handled in separate approved tasks.

This decision settles organization GUID generation in addition to the earlier account-specific rule. It does not silently decide identifiers for every future entity. Exact EF conversion for OrganizationCode, string comparison, SQL types/indexes, lifecycle fields, and registry relationships remain separate mapping work.

The subsequently approved implementation adds `Organization.Create(OrganizationCode, string)` in Organizations Domain, with a private constructor/private setters, application-generated PublicId, display-name guards, and `DateTimeOffset.UtcNow` creation time. Mapping, migrations, APIs, and database persistence remain unimplemented.

## Tests required with implementation

Verify valid creation, generated non-empty public identity, zero internal ID before persistence, code preservation, trimming, empty/null display-name rejection, 1/200-character accepted boundaries, 201-character rejection, and UTC creation time. Database uniqueness/FK behavior requires later SQL Server integration tests.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Organization-code rules](0005-organization-code-rules.md)
- [Account mapping design](../architecture/identity-account-ef-mapping-design.md)
- [Decision index](README.md)
