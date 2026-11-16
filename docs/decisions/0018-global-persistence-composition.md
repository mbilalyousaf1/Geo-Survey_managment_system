# 0018: Global persistence composition structure

Status: Accepted

Accepted and recorded: 2026-11-13

## Decision

The product owner approved the concrete structure described in the [control-plane design](../architecture/organization-mapping-and-control-plane-design.md):

- `GeoSurvey.Organizations.Infrastructure` owns Organization configuration and OrganizationCode conversion.
- Existing `GeoSurvey.Identity.Infrastructure` owns account and Identity configuration.
- `GeoSurvey.ControlPlane.Infrastructure` composes these mappings into `GlobalControlPlaneDbContext`.
- ControlPlane Infrastructure references module Infrastructure projects; modules do not reference ControlPlane back.
- Register mappings explicitly. Compose the account-to-organization foreign key in the control-plane integration layer.
- Maintain one global migration stream using `controlplane.__EFMigrationsHistory`. Tenant operational migration histories remain separate.
- Runtime application services receive use-case-specific persistence capabilities rather than unrestricted global-context access. Add those interfaces only with real approved use cases.

## Consequences and implementation boundary

This structure supplies a single reviewed integration point without permitting one business module to manipulate another's private tables. Database composition does not replace runtime authorization or application contracts. No module startup independently runs migrations.

Approval of names and dependency direction does not authorize generating every project, context, adapter, and migration at once. Implement the first useful mapping slice, explain packages and tests, and obtain code approval before significant implementation. Exact Identity context base type/supporting tables, provider versions, design-time configuration, and connection handling remain implementation-design work.

No project, package, DbContext, migration, database, or runtime registration is created by this documentation update.

Subsequent decision: [record 0019](0019-role-free-identity-context.md) resolves the Identity context base as `IdentityUserContext<IdentityAccount, int>`. Supporting-table details remain open; this does not change the accepted composition ownership or dependency direction.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Persistence ownership](0015-control-plane-persistence-ownership.md)
- [Organization mapping/materialization](0017-organization-mapping-and-materialization.md)
- [Decision index](README.md)
