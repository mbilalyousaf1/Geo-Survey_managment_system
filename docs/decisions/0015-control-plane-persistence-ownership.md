# 0015: Control-plane persistence ownership and organization key contract

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

The combined global identity/registry database needs coordinated migrations while preserving module ownership. Account mapping requires a real organization foreign-key target. The product owner approved the composition approach and table/key contract below.

## Decision

- Organizations owns its organization entity, code rules, and organization table configuration.
- Identity owns account entities and Identity table configuration.
- A control-plane Infrastructure project composes module-owned mappings into one global DbContext, with one migration history for the global database.
- Module runtime interactions use explicit application contracts. Shared persistence composition does not authorize a module to access another module's private tables or DbContext operations.
- Map organizations to `organization.Organizations`, with database-generated integer `Id` and unique public GUID `PublicId`.
- `identity.Accounts.OrganizationId` references `organization.Organizations.Id`, using the previously approved NO ACTION delete behavior.
- The single global migration stream coordinates organization table creation before the account FK. Tenant operational database mappings/migration histories remain separate.

## Consequences

The composition project is a persistence integration point, not a new business module or a license to mix business ownership. Module mapping registration must be explicit. The implementation must define how a module receives access only to its owned persistence operations; exposing the entire global context to every application service would defeat the boundary.

No duplicate organization entity, placeholder table, independent competing global migration stream, or startup auto-migration is approved. Migrations must be reviewed and applied through the specification's controlled deployment process.

Minimal organization fields, display-name rules, and application GUID generation were subsequently approved in [record 0016](0016-minimal-organization-entity.md). Exact code mapping, lifecycle state, registry fields, and deletion/retention behavior remain separate decisions.

Exact project/DbContext names, dependency references, module mapping entry points, migration-history table naming, design-time factory, packages, and scoped persistence interfaces will be proposed before implementation. No global context, EF Core package, entity, migration, or database is created by this documentation update.

## Required later verification

Verify composed-model ownership, absence of accidental duplicate tables, the real FK with NO ACTION behavior, shared global migration ordering, and runtime dependency boundaries. Use SQL Server integration tests for referential integrity. Existing object tests do not verify these unimplemented mappings.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Account SQL mapping choices](0014-account-sql-mapping-choices.md)
- [Account mapping design](../architecture/identity-account-ef-mapping-design.md)
- [Decision index](README.md)
