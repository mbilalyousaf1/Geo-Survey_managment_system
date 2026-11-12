# 0003: Separate accounts per organization

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md), OPEN-001, left a choice between one global login with organization memberships and fully separate organization credentials. The product owner selected separate accounts per organization and approved recording the decision before authentication or database implementation.

## Decision

A person has an independent account and credentials in each organization they use. The same email address may be used for distinct accounts in different organizations; it does not establish a shared identity or authorization link.

Preserve DB-001: identity records and the tenant registry remain in the combined global control-plane database. Organization-scoped identities in global storage are compatible with independent accounts. This decision does not move credentials into tenant operational databases.

## Consequences

- Account lookup and authentication must distinguish organization scope; email alone cannot identify an account across the platform. The exact lookup key and constraints still need design.
- A matching email address must not automatically link accounts, synchronize passwords, enable organization switching through one authenticated identity, or grant access to another organization.
- Recovery and credential/session operations must identify the intended organization account. Their exact flows, token structure, and storage are not defined by this record.
- Account identifiers used for authorization must distinguish separate accounts, even when they represent the same person. Tenant resolution and resource authorization remain server-side requirements.
- No authentication endpoints, identity packages, tables, migrations, or tenant provisioning behavior are implemented by this documentation task.

## Decisions still required before affected implementation

- Organization selection was subsequently resolved: [record 0004](0004-organization-code-login.md) specifies organization code + email + password for web and mobile; [record 0005](0005-organization-code-rules.md) defines code validation, normalization, uniqueness, selection, and immutability. Reserved codes and retired-code reuse remain open.
- Exact account schema, normalization, and uniqueness constraints within an organization; email reuse across organizations must remain possible.
- Platform-administrator identity scope and separation from organization accounts.
- Account recovery, onboarding, and session/token design consistent with the master specification.

These details are intentionally unresolved; resolving OPEN-001 does not approve them implicitly.

## Alternatives considered

One global login with memberships would allow a shared identity across organizations. The product owner selected independent organization accounts instead.

## Related documentation

- [Decision index](README.md)
- [Repository structure and tenant security boundary](../architecture/repository-structure.md)
