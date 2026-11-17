# 0026: Bootstrap console and operator access

Status: Accepted

Accepted and recorded: 2026-11-16

## Decision

Provide a separate `GeoSurvey.Bootstrap` console tool, not an API registration endpoint. The approved tool will:

- Reuse the ControlPlane context, accepted password normalization/blocklist, and Identity hashing settings.
- Read and confirm the password through masked interactive prompts; no password argument or default credential.
- Call a narrow creation contract/service implementing the approved transaction and application lock.
- Exit without modification when an administrator already exists.
- Leave MFA enrollment required before platform administration is permitted.

Operator authorization uses deployment access as its boundary: only an operator permitted to run the tool and obtain a dedicated bootstrap database credential can execute it. The API runtime credential should not receive bootstrap-specific privileges. Exact database grants/principal configuration and credential delivery remain to be explained in the concrete implementation plan; this decision does not assert that ordinary table DML permissions independently enforce account-scope authorization.

The implementation will involve the small console project, narrowly scoped creation contract/service, SQL Server integration tests, and operator runbook. No command, credential, database principal/grant, account, or endpoint is created by this documentation update.

## Remaining guardrails

Implementation update, 2026-11-17: the [console and operator runbook](../development/bootstrap-console.md) now exist, wrapping the tested service with masked confirmation, a dedicated environment connection, safe exit codes, and cancellation. Only help/rejection/synthetic-input terminal checks were executed; no real operator creation was performed. MFA/login and production credential provisioning remain separate work.

Subsequent decision: [record 0027](0027-permanent-bootstrap-completion.md) accepts permanent bootstrap completion even after administrator deletion/lockout. The earlier open question below is resolved by that rule; recovery implementation and the marker's concrete schema/code remain pending.

Disabled/deleted administrator and disaster-recovery behavior remains unresolved. An existence query cannot distinguish a never-bootstrapped system from one whose last administrator was deleted; do not silently make bootstrap a replacement/recovery mechanism. Resolve the durable one-time completion rule before enabling creation.

Concrete file/project placement, runtime/console DI composition, input/cancellation handling, exit codes, database privileges, and tests remain part of the implementation plan. Keep significant code changes subject to the existing review/approval workflow.

## References

- [Bootstrap command principles](0007-first-platform-administrator-bootstrap.md)
- [Bootstrap transaction/lock](0022-bootstrap-transaction-and-lock.md)
- [Administrator claim](0021-platform-administrator-claim.md)
- [Password blocklist implementation](../development/local-password-blocklist.md)
- [Decision index](README.md)
