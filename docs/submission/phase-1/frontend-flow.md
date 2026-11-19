# Phase 1 frontend handover

The Angular client covers the approved Phase 1 path through the real `/api/v1` contracts: Platform Administrator password entry, manual TOTP setup, ten recovery codes shown once, fresh password/authenticator or password/recovery sign-in, organization registry/create/status/retry, initial-administrator invitation, recipient password activation, organization sign-in, and database-backed workspace setup. Surveys, maps, project management, and user/role management remain outside this phase.

## Demo flow

1. Open `/platform/sign-in` on the local HTTPS demo and enter the operator-bootstrapped Platform Administrator credentials.
2. For first use, add the displayed key manually to a time-based authenticator (SHA-1, six digits, thirty seconds), then confirm with its code. The provisioning URI is available in a collapsed details control. Save all ten displayed recovery codes privately and acknowledge saving them.
3. Sign in afresh using the password and a new authenticator code. An already accepted authenticator time step cannot be reused; wait for the next code. A password-verified challenge may instead use one unused recovery code.
4. Create an organization with a unique code, display name, configured region, and initial administrator email. Its page polls the real lifecycle every six seconds. A Failed organization exposes a provisioning retry action.
5. At AwaitingAdministrator, issue the single-use, 24-hour invitation and copy the private fragment-based link. Replacement revokes any earlier unused invitation. The UI clears the issued link on hide/navigation.
6. The recipient opens `/activate#TOKEN`, confirms the organization/email, and chooses a unique 15–128-character password. The fragment is removed before inspecting the token through the API. The password is checked by the server's existing NFC/length/blocklist policy.
7. The recipient signs in using organization code, email, and their password. `/workspace` displays only the actual API-returned organization reference, region, schema version, identity-marker verification, and administrator grant. The UI rejects a returned identity that differs from the signed-in organization.

## Engineering and security

- `core/auth/auth.service.ts` holds access credentials and restricted MFA challenges only in memory. `auth.interceptor.ts` attaches access tokens only to this API's resource requests and retries an unauthorized request once after a successful refresh. Route guards are navigation aids; the backend remains the authorization authority.
- Cookie-changing operations are coordinated using the same Web Lock. A tab-local promise joins concurrent refresh attempts. A browser without Web Locks requires fresh sign-in at expiry, avoiding an uncoordinated single-use refresh race. New tabs on that browser also require fresh sign-in.
- BroadcastChannel messages contain only scope and public session ID. A different sign-in/session or matching sign-out invalidates existing views. Expected session IDs on refresh/logout prevent a stale tab from silently adopting or revoking a replacement family even when channel delivery is delayed.
- Auth and activation calls obtain `GET /auth/csrf`, then use `X-CSRF-TOKEN` without bearer authorization. Refresh credentials remain backend-managed host-only HttpOnly/Secure/SameSite cookies. Tokens, passwords, setup secrets, invitations, and recovery codes are never put in localStorage/sessionStorage, logs, or URL query strings.
- The activation token is retained in component memory until successful completion or navigation. Refreshing the stripped activation URL loses the token; reopen the private link to resume before it is consumed. Refreshing a transient MFA route returns to password entry.
- Forms use native labels/validation, semantic headings, alerts/status announcements, keyboard focus styles, a skip link, route focus, reduced-motion support, and responsive CSS. No external fonts, analytics, or UI component library are loaded.
- Production CSS loads using an ordinary stylesheet link. `optimization.styles.inlineCritical` is false because Angular's default deferred-link inline `onload` handler is blocked by the HTTPS host's script CSP. Browser acceptance checks the real computed layout before entering credentials.

## Tests and commands

Run from `src/Frontend/geosurvey-web`:

```powershell
npm run build
npm test -- --watch=false
npm run test:totp
npm run test:e2e:typecheck
npm run format:check
npm audit --omit=dev --json
```

`npm run test:e2e` requires the live local HTTPS Docker demo and `GEOSURVEY_ACCEPTANCE_CREDENTIALS` pointing to the seeder's private JSON `{email,password,baseUrl}`. Full acceptance requires a fresh, MFA-unenrolled synthetic platform account and creates two disposable organizations. After enrollment the runner adds only `totpSecret` to that private fixture for diagnostic resumption; recovery codes are never persisted. A resumed run explicitly lists fresh-enrollment/recovery checks as skipped and cannot prove the complete fresh flow. Install Chromium with `npx playwright install chromium`. The test uses actual browser controls and actual API responses, independently computes authenticator-device codes, checks separate same-email passwords/organization identities, and exercises refresh in simultaneous tabs. It never fakes provisioning or workspace data.

Browser traces, videos, screenshots, and error-context snapshots are disabled. The custom reporter omits raw errors, call logs, attachments, fixture values, and page contents. Its safe summary is `test-results/phase-1-browser-summary.json`; copy only that sanitized artifact into submission evidence. Test discovery/typechecking alone is not a passing browser acceptance result.

## Current verification record

On 2026-11-18, the final source production build passed (285.81 kB initial JavaScript/CSS; `dist/geosurvey-web/browser`), all 24 frontend tests passed in eight files, and Prettier check passed. The first frontend test run had four asynchronous fixture-rendering assertion failures; those assertions were corrected and rerun. The test-only authenticator passed both RFC-vector/malformed-input checks. Playwright harness typechecking and one-test discovery passed.

The initial HTTPS Docker browser runs completed enrollment/ten-code delivery, fresh TOTP sign-in, secure-cookie/memory-only checks, and provisioning/activation/sign-in/actual workspace checks for two organizations. They exposed duplicate error announcements after rejected credentials: clearing the sensitive field also displayed a required-fields alert. The UI now announces one server rejection, covered by real DOM regression tests for password and recovery-code failures. Public desktop/mobile visual checks also found and verified the production CSS/CSP fix. A later test exceeded the unchanged authentication limiter; the harness now paces actual auth/CSRF requests within that budget.

Final expanded acceptance on 2026-11-19 passed one complete real Chromium scenario in 87.102 seconds with eleven checkpoints and no skipped checks. It includes fresh enrollment, unused/consumed recovery-code behavior, both organization workspaces, hostile HTTP tenant selectors, Platform/Organization scope denials and real cross-tab refresh/session changes. See the [sanitized summary](evidence/browser-summary.json) and [validation report](validation-results.md). Final harness typechecking, full Prettier check and both independent authenticator tests passed. A clean extracted-source lockfile install and production build also passed; these results are separate from hosted CI, which was not run.

The full `npm audit --json` currently reports 28 transitive development-tool findings (14 moderate, 14 high), involving picomatch/glob/minimatch and their Angular CLI/build/Vitest dependency chains. `npm audit --omit=dev --json` passed with zero findings. The newly added Playwright runner has no reported advisory in that result. No broad Angular downgrade, forced audit rewrite, or unrelated package upgrade was performed. This is a remaining build-tool dependency maintenance limitation, not a claim of a clean full dependency audit.

The new development-only dependencies are Playwright `1.65.1` and Node 24 type definitions `24.19.2`. They are excluded from the production browser assets. API response types use the [shared contract](../../architecture/phase-1-integration-contracts.md); frontend run commands are also in the [app README](../../../src/Frontend/geosurvey-web/README.md).
