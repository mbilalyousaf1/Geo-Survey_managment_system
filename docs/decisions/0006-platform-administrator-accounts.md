# 0006: Platform Administrator account scope and login

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

Organization accounts are independent and use organization code + email + password. Platform Administrators create organizations and manage provisioning across the platform. Their account scope and login approach needed a separate decision. The product owner approved the approach below.

## Decision

- Platform Administrator accounts have an explicit platform scope, separate from organization accounts.
- Use a dedicated platform-admin login with email + password, without an organization code.
- Store these accounts in the combined global identity/tenant registry database, preserving DB-001.
- Access is limited to authorized platform operations, such as organization creation and provisioning management.
- Platform-admin status alone does not grant access to tenant operational data, including surveys.

## Consequences

The identity design must distinguish a platform account from an organization account. Matching email addresses must not imply shared credentials or authorization between these scopes. Separate scope and login do not require a separate database or a second identity system.

Server-side authorization must enforce platform permissions and tenant data boundaries. Successful platform authentication must not automatically create an organization session, bypass tenant authorization, or enable impersonation. No tenant support-access or impersonation workflow is approved by this record.

This documentation task does not implement authentication endpoints, UI, account classes, database mappings, permissions, migrations, or provisioning services.

## Details still to resolve

- MFA policy was subsequently approved in [record 0008](0008-platform-administrator-mfa.md): mandatory TOTP, enrollment before platform access, and single-use recovery codes. Exact implementation and reset/recovery workflow remain open.
- The first administrator's creation approach was subsequently approved in [record 0007](0007-first-platform-administrator-bootstrap.md): operator-run bootstrap with masked password entry and no overwrite/reset behavior. Exact implementation remains to be designed.
- Exact account schema, email normalization/uniqueness rules, and credential/session implementation.
- Dedicated login API/UI location and its error behavior.
- Subsequent platform-account management and recovery workflows.

The remaining implementation choices require separate design. The later MFA policy in record 0008 adds a required factor to the email/password login.

## Alternative considered

Treating a Platform Administrator as an ordinary organization account would couple platform authentication to organization selection. The approved approach uses an explicit platform scope and a dedicated login instead.

## Related documentation

- [Master specification, section 6.1](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Separate organization accounts](0003-separate-organization-accounts.md)
- [Organization login](0004-organization-code-login.md)
- [Decision index](README.md)
