# Phase 1 completion plan

Updated: 2026-11-19.

Submission priority update: the owner requested completion of all remaining Phase 1 work for submission **today**, selected a full local Docker demo plus submission package, and approved the [consolidated delivery plan](phase-1-submission-delivery-plan.md) and parallel agents. This authorizes implementation within that plan without repeated per-helper approvals. See [ADR 0041](../decisions/0041-phase-1-local-submission-delivery.md) and [shared integration contracts](../architecture/phase-1-integration-contracts.md).

## Current status — authoritative

The complete approved backend and Angular Phase 1 workflow is implemented: public Platform enrollment/TOTP/recovery sign-in; RS256/current-session authorization; strict refresh/logout; organization creation; durable outbox/Hangfire provisioning; tenant migrations and trusted marker/grant resolution; initial administrator activation; and the local HTTPS Docker stack/setup/package tooling. General RBAC, GIS/surveys and mobile integration remain later-phase work.

Current executed evidence: 631 backend tests passed with zero failures/skips; Release build passed with zero warnings/errors; formatting/analyzers passed; all 21 backend projects have no known vulnerable packages in the executed transitive NuGet audit. Frontend component/service tests passed 24/24. Final fresh Docker/Chromium acceptance passed one complete scenario with eleven checkpoints and zero skipped checks, including hostile HTTP tenant selectors and cross-scope denials. Clean extracted-source backend restore/build and Angular lockfile install/production build also passed. The [validation report](../submission/phase-1/validation-results.md) is the detailed evidence record.

The stack uses generated external private configuration and synthetic acceptance credentials only. No real administrator, production deployment, commit or push was performed. Hosted GitHub Actions execution is unverified. The [runbook](../submission/phase-1/runbook.md) and [known limitations](../submission/phase-1/known-limitations.md) are part of the submission.

The approved local Phase 1 exit workflow is complete. The submission includes the runbook, architecture, API collection, acceptance evidence and secret-scanned source manifest/archive. Production-only decisions and known limitations are documented. No later phase is authorized.

## Definition of completion

From master specification section 16: a Platform Administrator can create a test organization, provisioning creates its isolated operational database, and authenticated requests safely resolve the correct tenant. Foundational models and tests alone do not meet these exit criteria.

## Historical foundation progress

Everything below records earlier incremental work and its evidence. Pending-work statements describe those earlier slices; the authoritative status and submission validation report above supersede them.

- Separate organization accounts and explicit Platform/Organization scope.
- Organization value object/entity and Identity account/email components.
- Module-owned mappings and composed global context with reviewed constraints/FKs.
- Initial global migration, SQL review, disposable migration execution/reapplication tests.
- Scoped API context registration, connection validation, Docker packaging, liveness.
- Bootstrap password NFC/base-validation, shared hashing settings, and the approved verified local corpus/checker; command integration remains pending.
- Accepted administrator claim and transaction/application-lock designs, not yet implemented bootstrap behavior.

Verification evidence belongs to the individual development notes. The latest full backend run after local-corpus implementation passed 208 tests, with zero failures/skips. Build, formatting, and API publication checks also passed. Hosted CI has not been verified for these uncommitted changes.

## Remaining delivery order

These are proposed implementation slices, not approval of all technical details or new packages.

| Order | Slice | Required evidence / decision boundary |
| --- | --- | --- |
| 1 | Local password corpus and complete bootstrap password validation | Approved corpus/version/update/failure policy; real matching tests; no network dependency during validation |
| 2 | Operator command packaging and authorization design | Masked interactive password; approved way to authorize the deployment operator; no public registration |
| 3 | Atomic first-administrator creation | Reviewed contract/implementation; real SQL concurrency, rollback, timeout, rerun/no-reset tests |
| 4 | MFA enrollment and recovery-code storage design | Secret protection/key handling, restricted pre-enrollment state, attempt/replay limits, atomic recovery-code use |
| 5 | Administrator authentication and authorization | Reviewed JWT/session/refresh behavior; password+MFA verification; Platform scope plus administrator claim; denial before MFA and for organization accounts |
| 6 | Organization registry/provisioning state design | Required region/residency inputs and allowed configuration resolved; immutable code; public IDs; idempotent/resumable provisioning |
| 7 | Authorized organization creation | Input validation, permission enforcement, appropriate migrations and API contracts; no client-chosen database targets |
| 8 | Isolated tenant database provisioning | Trusted naming/connection references, tenant schema baseline, controlled migration execution, failure/resume tests |
| 9 | Trusted tenant resolver | Identity/registry-based selection, no client target trust, fresh scoped contexts and cross-tenant denial tests |
| 10 | Organization Administrator onboarding | Approved invitation/credential/activation workflow and organization-user credential rules; independent credentials |
| 11 | Client integration appropriate to the approved Phase 1 scope | Review minimal Angular administration/login/onboarding screens; no implicit mobile feature expansion |
| 12 | Phase 1 end-to-end acceptance | Bootstrap/enroll/login, create organization, provision database, onboard organization admin, prove trusted resolution/isolation, review errors/logs and local runbook |

Address decisions immediately before their affected implementation. Do not demand that the owner solve every later workflow in one questionnaire. Keep CI, docs, and relevant unit/integration/security checks synchronized with each approved slice. Commit/push permission remains separate.

## Next decision and implementation slice

Console update, 2026-11-17: [GeoSurvey.Bootstrap](bootstrap-console.md) is implemented and verified with 24 console tests, real terminal masking/cancellation checks, and a 247-test full backend run. No real operator account was created. Next, review the restricted MFA enrollment/login-session design before authentication implementation; later provisioning/resolution/onboarding exit criteria remain unmet.

Bootstrap service update, 2026-11-17: [the transactional creation service and permission script](bootstrap-creation-service.md) are implemented and verified only in disposable databases. The next slice is the console implementation/credential configuration plan; MFA and later Phase 1 workflows remain pending. Earlier pending-service statements below describe the preceding stage.

Marker storage update, 2026-11-17: [the singleton mapping and second migration](bootstrap-completion-storage.md) are implemented with fresh-install/upgrade/storage tests; latest full backend verification is 214 passing tests. Atomic creation/guard/lock integration and the operator command remain pending. Earlier pending-marker schema notes below describe the prior review stage.

Permanent-completion update: [record 0027](../decisions/0027-permanent-bootstrap-completion.md) resolves the lifetime rule: successful bootstrap remains recorded independently of account deletion/lockout. Next, review the small marker schema/migration implementation plan before wiring transactional creation and the console. Exact operator grants and recovery remain separate details.

Operator-command update: [record 0026](../decisions/0026-bootstrap-console-and-operator-access.md) accepts a separate console and deployment-operator/dedicated-credential access model. The next required business decision is whether successful bootstrap completion remains permanently recorded even after administrator deletion; exact grants and the implementation plan follow. No real account-creation code is enabled yet.

Progress update, 2026-11-16: [the local blocklist](local-password-blocklist.md) is now approved and implemented, completing slice 1's corpus/checker work. The next review is operator-command packaging and authorization before implementing account creation. Older pending-corpus wording below describes the preceding planning stage.

The local corpus, exact matching, checksum/failure rules, and update procedure are approved and implemented as documented in the [blocklist design](../architecture/local-password-blocklist-design.md). Future bootstrap integration must call the complete checker rather than treat the base validator alone as sufficient. Operator-command packaging and authorization are the next decisions.

Later unresolved choices include operator authorization, MFA/session/recovery implementation, region rules, and onboarding behavior. This plan records their dependency order without choosing business outcomes on the owner's behalf.

## References

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Bootstrap password components](bootstrap-password-components.md)
- [API context configuration](api-control-plane-configuration.md)
- [Bootstrap transaction/lock](../decisions/0022-bootstrap-transaction-and-lock.md)

Restricted MFA challenge update, 2026-11-17: [storage and lifecycle service](mfa-challenges.md) implement the approved first MFA slice. TOTP secret protection, enrollment completion, attempt limits, recovery, and administrator authentication remain pending.

Latest verification after restricted MFA challenge storage: 257 backend tests passed, zero failures/skips; Release build and formatting passed. See the MFA notes for the preceding bootstrap timing failure and rerun. Phase 1 acceptance remains pending.

TOTP protection update, 2026-11-17: the [account-bound secret protector](totp-secret-protection.md) is implemented. Host key configuration and durable authenticator-secret storage/enrollment remain pending; this component is not enabled in the API.

Verification after the TOTP protection component: 268 backend tests passed with zero failures/skips; restore, Release build (zero warnings/errors), formatting/analyzers, and whitespace checks passed. No production key configuration was enabled.

API key configuration update: [PFX-backed persistent host keys](data-protection-host.md) are implemented and register the account-bound protector. Authenticator-secret persistence, enrollment, attempt/replay controls, recovery, and login remain pending.

Latest local backend verification after host key configuration: 279 tests passed, zero failures/skips; Release build, formatting/analyzers, and whitespace checks passed. Deployment-specific certificate rotation and container restart verification remain pending.
