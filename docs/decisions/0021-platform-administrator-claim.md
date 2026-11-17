# 0021: Platform Administrator claim

Status: Accepted

Accepted and recorded: 2026-11-16

## Context

`AccountScope.Platform` distinguishes platform accounts from organization accounts but does not grant administrator permissions. The first-administrator bootstrap needs an explicit representation of administrator status without introducing global role tables.

## Decision

Store a server-managed claim in the existing `identity.AccountClaims` table:

| Field | Accepted value |
| --- | --- |
| Claim type | `geosurvey:platform-role` |
| Claim value | `administrator` |

The future bootstrap creates the Platform account and this claim atomically and refuses to proceed if a Platform Administrator already exists. An existence query alone does not make concurrent bootstrap safe; concurrency enforcement remains to be designed.

Future platform-administration authorization requires Platform scope, administrator status, and completed MFA. An organization account cannot obtain platform access from this claim alone. Claim management is server-controlled; no client-supplied claim or permission is trusted. Stored administrator status does not grant tenant operational access.

This representation uses existing claim storage and preserves the accepted role-free global Identity model. It does not decide individual platform permissions or later administrator-management workflows.

## Implementation boundary and open work

This records the approved representation only. No claim constant, policy, account creation service, command, endpoint, schema change, or migration is implemented by this documentation task.

Bootstrap concurrency/transaction design, password policy, operator authorization, command packaging, and MFA enrollment/session behavior remain separate review items. Disabled/deleted administrator behavior and recovery remain unresolved; bootstrap must not become an implicit reset or replacement workflow.

## Related documentation

Subsequent decision: [record 0022](0022-bootstrap-transaction-and-lock.md) resolves the transaction/application-lock approach with a 30-second timeout. Earlier statements about concurrency design being open describe this record's original scope; implementation remains pending.

- [First Platform Administrator bootstrap](0007-first-platform-administrator-bootstrap.md)
- [Mandatory administrator MFA](0008-platform-administrator-mfa.md)
- [Role-free Identity context](0019-role-free-identity-context.md)
- [Decision index](README.md)
