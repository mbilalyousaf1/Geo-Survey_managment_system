# 0041: Complete Phase 1 as a local Docker submission

Status: Accepted, 2026-11-18. The product owner approved the consolidated delivery plan and parallel agents. Implementation within that plan does not require repeated per-helper approvals; commit/push authorization remains separate.

## Context

The submission must demonstrate the complete Phase 1 workflow today. Earlier internal identity/MFA/session components did not establish public authentication, organization onboarding, actual tenant provisioning, or browser acceptance. The [approved plan](../development/phase-1-submission-delivery-plan.md) defines the delivery boundary.

## Decision

- Use one server-configured `local-demo` region/profile. Database names and connections come from trusted server configuration and registry data. Production regions/data residency remain open.
- Activate the initial Organization Administrator through a 24-hour random, digest-only, single-use invitation, shown once for manual secure handoff. Replacement invalidates the previous unused invitation and cannot reset an active account. Each organization retains an independent account/password even when email addresses match.
- Store the initial administrator grant in the tenant database. Commit that grant before enabling the global credential. Phase 1 does not implement general custom RBAC or organization-user MFA policy.
- Permit Platform password plus one unused recovery code as a substitute for TOTP, sharing the existing MFA attempt budget. Consume the code atomically with session creation; do not reset the authenticator or regenerate codes.
- Sign real access tokens with RS256 using a separate mounted PFX and secret-supplied password. Validate issuer/audience/signature/lifetime and current account, role, security stamp and session family on the server. Preserve five-minute access, thirty-minute refresh inactivity and eight-hour absolute limits.
- Rotate refresh tokens strictly once and revoke the family on reuse. Keep access tokens in browser memory and refresh credentials in host-only HttpOnly/Secure/SameSite cookies with CSRF protection. Coordinate browser refreshes and detect session changes across tabs.
- Persist organization creation and provisioning requests in one global transaction. Dispatch the outbox through BackgroundService to durable Hangfire SQL jobs in a separate worker. The public API's SQL principal cannot create databases. The worker validates database ownership, applies real migrations and verifies the immutable tenant marker before readiness.
- Use the existing standalone Angular application and ordinary CSS. Add no UI component library. Serve web/API through one local HTTPS origin with external generated certificates/configuration.
- Include actual backend/SQL/HTTP/browser evidence, a runbook, parameterized API collection, known limitations and a secret-scanned source archive. Keep credentials, keys, runtime volumes and sensitive browser traces out of source/images/submission.

## Consequences

The full local workflow is implemented with explicit module application contracts and real isolated tenant databases. No production residency, cloud deployment, retention policy, GIS/mobile business workflow or Phase 2 feature is approved by this decision. The [validation report](../submission/phase-1/validation-results.md) determines which checks actually ran; this decision itself is not passing evidence.

Provisioning preserves unknown databases and fails closed. A crash in the narrow interval between database creation and its ownership property requires controlled operator verification/repair before retry; it must never cause blind adoption or deletion. Production deployment also needs reviewed certificate/key rotation, backups/disaster recovery, hosting and operational procedures.

The framework authentication/OpenAPI packages implement real HTTP security/documentation; Hangfire packages provide durable SQL jobs; Playwright is development-only browser verification. Compatible transitive security pins and exact dependency-audit results are recorded in the validation report. No unrelated dependency mass upgrade is included.
