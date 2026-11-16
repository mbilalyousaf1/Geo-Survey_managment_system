# 0011: Single Identity account table with explicit scope

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

The product owner reviewed the [account-storage proposal](../architecture/identity-account-storage-proposal.md) and approved one Identity account table with explicit scope and scoped email uniqueness. [Record 0010](0010-aspnet-core-identity-ef-core.md) selects Identity with EF Core; separate account credentials and approved tenant boundaries remain unchanged.

## Decision

- Store platform and organization accounts in one Identity account table in the global control-plane database, with an independent row and credentials for every account.
- Represent Platform or Organization scope explicitly.
- Require `OrganizationId` for Organization scope and prohibit it for Platform scope. Reject invalid scope/organization combinations with database constraints; organization associations must reference an existing global organization.
- Enforce organization email uniqueness on `(OrganizationId, NormalizedEmail)` within Organization scope, and platform normalized-email uniqueness within Platform scope.
- Organization login selects only the resolved organization's account; platform login selects only Platform scope. Do not fall back across scopes or organizations.

The same email in two organizations and platform scope identifies three independent rows. A shared table grants neither credential sharing nor cross-tenant authorization.

## Consequences

All account lookups, validation, credential operations, and session flows must preserve scope. Email-only global lookup cannot identify accounts unambiguously. Database constraints enforce scope and uniqueness under concurrency; application pre-checks are insufficient.

Identity's globally unique username must not block approved email reuse. Account GUID generation and internal username format were subsequently approved in [record 0012](0012-account-identifiers-and-usernames.md). Validators/store customizations and normalization implementation remain open. No default Identity UI or generic endpoint is approved as an alternative login path.

No account class, table, EF mapping, migration, or package is implemented by this documentation update. Exact SQL names, lengths, persisted scope representation, collation, lifecycle/delete policies, and MFA/session mappings remain to be reviewed. Acceptance of the table strategy is not blanket acceptance of every candidate field or example in the proposal.

## Alternative considered

Separate physical platform and organization account tables were considered. The approved single-table strategy avoids duplicate account infrastructure, while requiring explicit scope constraints and careful query isolation.

## Verification required with implementation

SQL Server integration tests must verify scope checks, foreign keys, scoped email reuse and duplicate rejection, and concurrent constraint enforcement. Authentication tests must reject cross-scope and cross-organization credential lookup. Existing tests do not yet verify this unimplemented mapping.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Logical model proposal](../architecture/identity-data-model-proposal.md)
- [Decision index](README.md)
