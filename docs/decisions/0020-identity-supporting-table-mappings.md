# 0020: Identity supporting-table mappings

Status: Accepted

Accepted and recorded: 2026-11-13

## Context

[Record 0019](0019-role-free-identity-context.md) accepts a role-free Identity context. The product owner has now approved the supporting-table choices needed to preserve compatibility with the standard Identity user store.

## Decision

| Table | Purpose | Primary key |
| --- | --- | --- |
| `identity.AccountClaims` | Account-associated claims; not tenant permission storage | Framework claim `Id` |
| `identity.AccountLogins` | Framework external-login associations | Framework composite `(LoginProvider, ProviderKey)` |
| `identity.AccountTokens` | Framework named account tokens | Framework composite `(UserId, LoginProvider, Name)` |

- Select Identity schema Version 2. Exclude role/user-role tables and the Version 3 passkey table.
- Explicitly limit login `LoginProvider`/`ProviderKey` and token `LoginProvider`/`Name` to 128 characters.
- Preserve framework primary keys and required `UserId` foreign keys to `identity.Accounts`.
- Override account-to-supporting-row deletion to `NO ACTION` for all three relationships. Dependent rows block account deletion; no automatic cleanup or hard-delete workflow is approved. Retention and deletion business rules remain open.
- Apply schema-version and key-length choices consistently during runtime model creation, tests, and future migration tooling. Apply module-owned configuration after Identity base configuration.
- Identity Infrastructure owns these mappings; ControlPlane Infrastructure composes them into the accepted global context.

These are storage choices, not feature enablement. External login, claims-based authorization, MFA, refresh tokens, and protection of sensitive stored values still require separately reviewed behavior. Platform scope alone grants no administrative permission; tenant roles/permissions remain tenant-owned. The framework login key does not itself provide organization-scoped external-login behavior.

## Verification required with implementation

Test actual table names, primary/composite keys, 128-character limits, required account relationships, `NO ACTION` behavior, and the absence of role/passkey tables. Validate the final composed model and SQL Server behavior rather than assuming base configuration remains unchanged after overrides.

## Implementation boundary

This decision does not create or authorize an unplanned implementation of packages, configurations, context, tables, migrations, endpoints, or secret storage. The next task is a concrete implementation plan for one small persistence slice, with files, dependencies, security implications, and tests explained before code approval.

## Related documentation

- [Role-free context choice](0019-role-free-identity-context.md)
- [Identity account mapping design](../architecture/identity-account-ef-mapping-design.md)
- [Decision index](README.md)
