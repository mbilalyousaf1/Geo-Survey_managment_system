# 0004: Organization code at login

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

[Separate organization accounts](0003-separate-organization-accounts.md) permit the same email address in different organizations. Users must identify the intended organization account before authentication. The product owner approved organization code + email + password for the initial web and mobile login approach.

## Decision

Organization users provide organization code, email, and password on both clients. The server resolves the code through trusted tenant registry data, then validates credentials within the identified organization scope.

An organization code is an account-selection input, not proof of membership or permission. It must not be treated as a credential, a database name, or a connection string. Operational tenant context must be established from the authenticated account and trusted server-side identity/registry data. Supplying a different organization code must not switch an authenticated account into another tenant without authentication and authorization for that tenant.

## Consequences

- Web and mobile use a consistent organization-selection approach without requiring organization-specific domains for initial login.
- Email alone cannot select an account across organizations. The same email/password input must be checked only within the intended organization scope, not searched across tenants as a fallback.
- No organization lookup endpoint, login endpoint, request DTO, database constraint, identity package, or UI behavior beyond the approved input selection is implemented by this record.
- The combined global identity/registry database and separate organization operational databases remain unchanged.
- Platform-administrator login is a separate unresolved design question; this record covers organization users only.

## Details still to resolve

Code format, normalization, platform-wide uniqueness, administrator selection, and immutability were subsequently approved in [record 0005](0005-organization-code-rules.md). The exact database constraints remain to be implemented.

- Reserved codes and reuse after retirement/deletion.
- Login API, error behavior, recovery/onboarding, and credential/session implementation.

Examples such as `acme-surveys` must now follow the approved rules in record 0005.

## Alternative considered

Organization-specific web addresses were discussed. The product owner selected an explicit organization code for the initial approach; custom domains and subdomains are not introduced by this decision.

## Related documentation

- [Master specification, section 6.1](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Decision index](README.md)
- [Repository architecture](../architecture/repository-structure.md)
