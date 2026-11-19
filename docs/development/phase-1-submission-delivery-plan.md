# Phase 1 submission delivery plan

Status: Accepted implementation batch, 2026-11-18. The owner requested completion of all remaining Phase 1 work for submission today, selected a full local Docker demo plus submission package, and explicitly approved this plan and parallel agents. Deadline time is not specified. The choices below are accepted for this local delivery; production-only decisions remain open where stated.

Delivery update, 2026-11-19: the approved local workflow is implemented and verified by the final fresh eleven-checkpoint browser scenario, 631 backend tests, 24 frontend tests and clean extracted-source builds. The [submission guide](../submission/phase-1/README.md) and [validation report](../submission/phase-1/validation-results.md) contain the handover. Starting-gap and proposed-design wording below is the original accepted plan's historical context.

## Completion target and execution agreement

Demonstrate the actual Phase 1 exit path: bootstrap a Platform Administrator, enroll/sign in with MFA, create an organization, provision its separate versioned database, activate its initial Organization Administrator, sign in with that organization's credentials, and prove requests use only its authorized database. Deliver the Angular workflow, local HTTPS Docker stack, automated acceptance evidence, API documentation, and submission runbook/package.

Implement this as one approved Phase 1 delivery effort, keeping changes in coherent internal steps with milestone explanations and verification. Once the owner approves this plan, do not stop for another approval after each helper/class/task within it. Ask again only for an unresolved business decision or a material departure from this plan. Documentation stays autonomous; commit/push permission remains separate. Do not enter Phase 2 business features.

Parallel agents were explicitly authorized and used for independently owned backend/frontend/test areas, with shared contracts and coordinated integration/build verification.

## Starting evidence and actual gaps

The latest completed internal session slice passed 595 backend tests and Release/format checks. That evidence does not prove public authentication, tenant provisioning, frontend flows, or Phase 1 acceptance. The API currently exposes liveness only; Angular routes are empty. The existing database stores account/organization identity and MFA/session foundations, but no tenant registry/provisioning baseline or administrator activation flow yet exists.

The installed SDK is 10.0.301, Node is 24.14.1, and Docker's Linux engine is running. These were inspected for this delivery plan; no application tests were rerun for documentation preparation.

## Proposed decisions to approve together

| Area | Recommended decision for this delivery |
| --- | --- |
| Region/residency, OPEN-011 | Configure one local-demo region/profile on the server. Organization creation selects only this allowed profile; database names/connections are derived server-side. This makes no production data-residency compliance claim. Real production regions/hosting remain a later deployment decision. |
| Organization onboarding | An authorized Platform Administrator issues a 24-hour, random, hashed, single-use activation link, shown once for manual secure handoff. The recipient sets their own password using the existing NFC/length/local-blocklist policy. Reissue revokes an unused old invitation and never resets an already active account. No email provider or shared/default password is introduced. |
| Initial organization authorization | Organization code + email + password; separate accounts remain mandatory. Reuse the five-wrong-password/15-minute lockout for this entry. Phase 1 grants only initial Organization Administrator access stored in that tenant's own database; full custom RBAC stays in Phase 2. Organization-user MFA rollout remains undecided. |
| Platform recovery sign-in | A password-verified Verification challenge plus one unused issued recovery code may replace TOTP for sign-in. Consume the code atomically with session creation, share the existing MFA failure budget, leave TOTP configured, and do not enable automatic MFA reset or code regeneration. Accept canonical issued formatting after surrounding trim and invariant uppercase conversion; reject other malformed formats. |
| Refresh/logout | Strict single-use rotation. Reuse of a consumed refresh token revokes that family, with no replay grace period. Recheck the session/current account on authenticated requests so revoked families lose access immediately. Logout revokes the current family. Existing 5-minute access/30-minute refresh/8-hour absolute policy remains unchanged. |
| JWT keys and browser transport | RS256 with a separately generated/mounted RSA signing PFX and secret-supplied password; no reuse of the Data Protection key. Validate signature, issuer, audience, expiry and server-side session/account binding. Keep web access tokens in memory and refresh credentials in host-only HttpOnly/Secure/SameSite cookies with CSRF protection. Use one HTTPS browser origin. |
| Provisioning and readiness | Durable Pending/Provisioning/AwaitingAdministrator/Ready/Failed lifecycle, with Suspended enforced if present. Ready requires successful tenant migrations/health and initial administrator activation. Use bounded retries, safe failure codes, server-generated database names, resumable/idempotent jobs, and a durable outbox to avoid losing provisioning requests. |
| UI, OPEN-012 | Use the existing Angular standalone components, forms, services/RxJS, and ordinary CSS. Add no component library for this submission. |

The local region and UI choices resolve only this delivery's configuration, not production hosting/residency or a later product-wide UI-library selection. No pricing/subscription model, deletion/retention rule, tenant impersonation, GIS/survey feature, or mobile expansion is selected.

## Delivery steps within the single approval

### 1. Complete real authentication and authorization

- Wire password entry, server-generated enrollment material, confirmation and one-time recovery-code delivery, and actual session start to versioned endpoints.
- Add real access-token signing/validation, account/family/stamp/scope checks, secure refresh rotation/reuse handling, logout, CSRF defenses, and authentication rate limiting. Preserve the accepted password/MFA budgets and replay controls.
- Add password-plus-recovery-code sign-in with atomic single use; show no fake access token or placeholder success for unfinished authentication.
- Keep Platform and Organization accounts and authorization paths distinct. A Platform Administrator cannot query tenant operational data through their platform session.

### 2. Implement registry, durable provisioning, and tenant resolution

- Extend the global model with trusted region/profile references, registry/provisioning state, invitation records, durable provisioning requests/outbox, and appropriate security/provisioning audit records. Create and test forward migrations; do not seed real credentials.
- Give the API only its runtime permissions. Run tenant database creation/migration in a separate provisioning-worker process with separately configured credentials, rather than granting the public API database-creation privileges.
- Use the already selected Hangfire/SQL Server durable job pattern, plus a small BackgroundService outbox dispatcher. Only server-managed public organization references enter jobs; payloads contain no connection strings/credentials.
- Create the tenant database from a versioned minimal baseline containing its immutable organization identity marker and initial-administrator authorization storage. Verify the marker against the trusted registry when opening a tenant context. Do not add empty later-feature schemas or fabricated survey data.
- Resolve every tenant request from the signed subject plus current global account/registry data, create a fresh scoped operational context, and enforce the tenant's initial administrator grant. Client headers/query strings must never choose database targets.
- Make retries safe after partial creation, worker restarts, and failed migrations; preserve failed databases for controlled resumption instead of automatically deleting work.

### 3. Complete onboarding and the Angular workflow

- Implement scoped initial-account creation and invitation/activation without overwriting an existing account or sharing credentials across organizations.
- Persist the tenant administrator grant before enabling the global account's usable password; make partial activation resumable and fail closed without a distributed transaction or an active account missing its grant.
- Provide Platform login/enrollment/recovery screens, a real organization list/create/status/invitation workflow, activation/password setup, organization login, and a tenant setup/workspace page backed by its actual database marker.
- Keep errors usable and sanitized. Use public GUIDs in routes; never display database names, connection strings, integer identifiers, or stored credential material.
- Browser refreshes must be coordinated to avoid legitimate same-cookie races under strict rotation. Detect account/session changes across tabs and return to the correct sign-in flow instead of silently switching an existing workspace to another account.

### 4. Make the local stack and submission repeatable

- Add a dedicated Phase 1 Compose stack for SQL Server, API, provisioning worker, and Angular/static HTTPS host. Preserve the existing foundation Compose workflow.
- Provide scripts to generate local-only secrets/certificates outside source/images, initialize migrations/permissions, start the stack, run acceptance checks, and stop only the demo/test resources created by that run. Never commit real keys, passwords, tokens, or local environment files.
- Preserve the masked one-time operator bootstrap command. Do not add a public bootstrap/administrator-registration endpoint or default credentials. Automated acceptance fixtures may create synthetic accounts only in their own disposable environment.
- Update OpenAPI and a parameterized Postman collection, Docker/build contexts, solution/project dependencies, architecture rules, CI, and relevant docs together. Explain each newly introduced dependency.
- Package the worktree source, configuration examples, migration artifacts, tests, and submission documentation while excluding secrets, runtime volumes, node_modules, bin/obj, and sensitive test traces.

### 5. Run and record acceptance

Run the backend Release build, unit/SQL/API/architecture checks, frontend production build/tests/formatting, full Docker startup and HTTP flows, and real browser end-to-end checks. Capture actual results; the earlier 595 internal-test result is not the final delivery result.

Acceptance must prove at least:

1. Operator bootstrap, actual MFA enrollment, one-time backup delivery, password+TOTP login, and password+unused-backup login work through the real interfaces.
2. Wrong codes/passwords, replay, expired/replaced challenges, consumed recovery codes, and revoked refresh families fail safely; failed writes preserve transactional consistency.
3. Authorized organization creation reaches the appropriate real provisioning/activation states; restart/retry preserves identity and does not create duplicate databases/accounts.
4. Two organizations have separate physical databases and separate same-email credentials. Each administrator sees their own setup marker; attempts to substitute the other's public ID/header/database selector fail.
5. An unauthenticated caller, wrong scope/role, inactive/suspended organization, corrupted registry/database-marker binding, or removed tenant grant cannot access the workspace.
6. A fresh checkout can follow the runbook and launch the local demo without committed/default credentials. The submission package contains no secret material or invented passing evidence.

Disable credential-bearing browser traces/video and avoid screenshots of authenticator/recovery material. Retain sanitized acceptance summaries and safe workspace/provisioning screenshots where useful.

## Affected components and dependencies

| Component | Expected changes |
| --- | --- |
| Identity Infrastructure and ControlPlane Infrastructure | Access-token implementation; enrollment/recovery/public-flow adapters; refresh/logout/current-session checks; scoped organization login and activation; global registry/outbox/audit models and migrations |
| Application contracts | Add only the Identity, Organizations, and Tenancy application contracts actually used by API, worker, and adapters; keep EF/framework types out of those contracts and Domain dependencies clean |
| New Tenancy Infrastructure | Trusted resolver/context factory, tenant baseline/migrations, provisioning implementation, and local administrator-grant persistence |
| New provisioning-worker executable | Hangfire job host, bounded durable work and outbox dispatch; owns privileged provisioning configuration separately from API |
| GeoSurvey.Api | Real versioned auth/organization/activation/tenant endpoints, authorization, CSRF/rate limits, safe ProblemDetails, DI composition, and OpenAPI |
| Existing Angular app | Authentication, platform organizations, activation and tenant workspace features; routing, HTTP/refresh coordination, forms and frontend tests |
| Tests/deploy/scripts/CI | Global/tenant/API/security/browser acceptance; Dockerfiles/Compose/bootstrap setup; secret-safe packaging; expanded architecture checks and validation pipeline |
| Documentation | Accepted decisions, schema/API descriptions, handover, demo script, actual validation evidence, remaining production-only decisions, and Phase 1 progress |

New dependencies are limited to demonstrated needs: ASP.NET Core JWT Bearer and IdentityModel token libraries for real JWT validation/issuance; ASP.NET Core OpenAPI for API documentation; .NET hosting/Hangfire NetCore/SQL Server support for the separate durable worker; and Playwright as a development-only browser acceptance runner. Select compatible patched versions and record the exact versions during implementation; do not mass-upgrade unrelated dependencies or add paid Hangfire products.

## Submission outputs

Maintain docs/submission/phase-1/ with a concise architecture overview, schema/flow diagrams, start/bootstrap/demo instructions, API collection, acceptance matrix, actual validation results, known limitations, and a file manifest. Generate a secret-free submission archive under artifacts/phase-1/ after the final checks. A plan, internal test count, or unexecuted checklist alone is not Phase 1 completion.

## References

- [Master architecture and Phase 1 baseline](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Current Phase 1 implementation progress](phase-1-completion-plan.md)
- [Accepted session start/lifetimes](../decisions/0040-platform-session-start-and-lifetimes.md)
- [ASP.NET Core JWT Bearer documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [Hangfire background processing documentation](https://docs.hangfire.io/en/latest/background-processing/index.html)
- [Playwright browser acceptance runner](https://playwright.dev/docs/intro)
