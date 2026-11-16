# 0019: Role-free Identity context base

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

The global context composition is accepted in [record 0018](0018-global-persistence-composition.md), but its Identity base type was unresolved. Organization roles and permissions belong in tenant databases; a default global role model would not match that boundary.

## Decision

The future `GlobalControlPlaneDbContext` will extend `IdentityUserContext<IdentityAccount, int>` rather than the role-enabled `IdentityDbContext`.

- Preserve Identity's base model configuration, including concurrency handling, then apply the module-owned account overrides explicitly.
- Keep Organization mapping and account-to-organization FK composition in ControlPlane Infrastructure as already accepted.
- Review concrete supporting claims, login, and token tables before creating them. Exact table names, key lengths, Identity schema-version options, and any version-specific additional entities remain unapproved.
- Do not introduce global organization role or user-role tables. Platform account scope alone does not grant administrative permission.

This base choice does not implement claims-based authorization, external login, MFA, session/refresh-token storage, or protection of stored secrets. Those behaviors and mappings require separate review. Supporting framework tables do not automatically enable features.

## Implementation boundary

Approval selects the context approach only. Packages, production context, account configurations, supporting tables, migrations, registration, and endpoints still require a concrete approved implementation plan. Domain remains independent of Identity and EF Core.

Organization mapping and SQL Server tests are implemented separately; Identity mappings and global composition remain pending.

Subsequent decision: [record 0020](0020-identity-supporting-table-mappings.md) accepts the supporting-table names, keys/lengths, Version 2 schema, and NO ACTION relationships. The earlier statements about these choices awaiting review describe the scope at acceptance of this record; implementation still requires a concrete approved plan.

## References

- [Microsoft Identity model customization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0)
- [Identity account mapping design](../architecture/identity-account-ef-mapping-design.md)
- [Decision index](README.md)
