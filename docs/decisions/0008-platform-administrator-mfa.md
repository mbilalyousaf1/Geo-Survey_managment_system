# 0008: Mandatory Platform Administrator MFA

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

[Record 0006](0006-platform-administrator-accounts.md) defines platform-scoped administrator accounts and a dedicated email/password login. [Record 0007](0007-first-platform-administrator-bootstrap.md) defines initial account creation. The product owner approved mandatory MFA for these privileged accounts.

## Decision

- Every Platform Administrator must use an authenticator app with time-based one-time passwords (TOTP).
- Complete MFA enrollment before allowing platform administration.
- Require password plus an authenticator code at login.
- Provide single-use recovery codes.
- Design MFA reset and administrative recovery as a separate workflow.

Organization-user MFA remains undecided. This policy does not impose organization-user enrollment or select an authentication package/provider.

## Consequences

Creating a platform account through bootstrap does not by itself permit platform administration: the administrator must complete MFA enrollment. A password-only or incomplete enrollment session must not carry platform-administration permissions. Exact enrollment and session state transitions remain to be designed.

Recovery codes must be single-use; their issuance, protection, consumption, and role in the recovery flow require implementation design. Providing recovery codes does not approve a password-only bypass, an unrestricted MFA reset endpoint, or reuse of bootstrap to reset credentials.

This documentation task implements no enrollment UI, TOTP verification, recovery-code storage, authentication endpoints, schema, migrations, or token/session behavior.

## Remaining design work

- Enrollment confirmation and session restrictions before MFA completion.
- TOTP parameters, replay/attempt controls, secret protection, and selected implementation.
- Recovery-code generation, storage, regeneration, and concurrent single-use enforcement.
- MFA reset and administrative recovery authorization and verification.
- Exact login API/UI and session/token integration.

These details are not resolved implicitly by approving the policy.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Platform Administrator scope and login](0006-platform-administrator-accounts.md)
- [First administrator bootstrap](0007-first-platform-administrator-bootstrap.md)
- [Decision index](README.md)
