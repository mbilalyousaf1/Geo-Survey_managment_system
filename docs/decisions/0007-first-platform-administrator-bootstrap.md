# 0007: First Platform Administrator bootstrap

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

[Record 0006](0006-platform-administrator-accounts.md) defines separate platform-scoped administrator accounts. The first such account needs a controlled creation path before ordinary platform administration is available. The product owner approved the operator-run approach below.

## Decision

- An authorized operator runs a one-time administrative bootstrap command in the deployment environment.
- The command creates the first platform-scoped administrator only when no Platform Administrator exists.
- Read the password through a masked interactive prompt. Do not accept it as a command-line argument or committed configuration, and do not supply default credentials.
- Use the application's password-hashing mechanism; do not store or log the plaintext password.
- Re-running bootstrap cannot overwrite an existing account or reset credentials.
- Do not expose a public administrator-registration endpoint.

## Consequences

Bootstrap is an operational action, separate from normal login and organization onboarding. It requires access to the intended deployment environment and global identity database. Creating a Platform Administrator does not grant access to tenant operational data.

The eventual implementation must enforce the first-account condition safely when attempts run concurrently; a simple unchecked existence query is insufficient. The exact transactional guard and operator authorization mechanism need design. Disabled/deleted administrator handling and disaster recovery must not silently turn this command into a credential-reset or replacement-admin workflow.

Password entry must not expose secrets in process arguments, logs, source control, or example commands. The interactive approach does not approve an unattended bootstrap mode. This decision selects no password-hashing package or password policy.

No bootstrap executable, account creation service, identity package, endpoint, migration, or database change is implemented by this documentation task.

## Remaining decisions

Permanent one-time completion was subsequently accepted in [record 0027](0027-permanent-bootstrap-completion.md): commit a durable marker atomically with account/claim creation; deletion or lockout does not re-enable bootstrap. Recovery remains separately authorized and unimplemented.

Console packaging and operator boundary were subsequently accepted in [record 0026](0026-bootstrap-console-and-operator-access.md). A separate console tool uses deployment access and a dedicated bootstrap credential; exact grants/credential setup and the concrete code plan remain pending. Disabled/deleted administrator behavior must be resolved before creation is enabled.

Base password rules and hashing were subsequently accepted in [record 0023](0023-bootstrap-base-password-policy.md). The blocklist source/operations, concrete integration, and remaining operational/security choices remain open; earlier open-policy statements describe their original stage.

Concurrency design was subsequently accepted in [record 0022](0022-bootstrap-transaction-and-lock.md): a transaction-owned application lock with a 30-second timeout, existence recheck under lock, and atomic account/claim creation. Earlier references to the transactional guard being open describe the original decision stage. Implementation and the other security/operational choices remain pending.

Administrator representation was subsequently approved in [record 0021](0021-platform-administrator-claim.md): Platform scope plus a server-managed `geosurvey:platform-role` / `administrator` claim. Bootstrap must create account and claim atomically; administration also requires completed MFA. Concurrency enforcement and the remaining command/security choices below still need review.

- MFA policy was subsequently approved in [record 0008](0008-platform-administrator-mfa.md); enrollment implementation and reset/recovery workflow still require design.
- Exact command packaging, operator authorization, and deployment runbook.
- Identity schema, password policy/hashing implementation, and concurrency enforcement.
- Later administrator creation, account recovery, and behavior after administrator disablement or deletion.

These are separate design tasks, not implicitly resolved by approving bootstrap.

## Alternatives considered

Public registration and default seeded administrator credentials do not match the approved controlled creation approach. Reusing bootstrap to overwrite accounts or reset passwords is explicitly excluded.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Platform Administrator scope and login](0006-platform-administrator-accounts.md)
- [Decision index](README.md)
