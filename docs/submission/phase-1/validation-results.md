# Phase 1 validation evidence

Final delivery update: 2026-11-19 (Asia/Karachi). Scope: the owner's approved full local Docker submission, not production deployment. Backend results below were established during the 2026-11-18 delivery work; the final expanded browser and clean extracted-source checks completed on 2026-11-19.

## Executed backend checks

| Check | Exact command | Result |
| --- | --- | --- |
| Final Release build | `dotnet build GeoSurvey.sln --configuration Release --no-restore -m:1` | Passed; zero warnings/errors |
| Full real SQL/API/backend suite | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1` | 631 passed, zero failed/skipped |
| Formatting/analyzers | `dotnet format GeoSurvey.sln --verify-no-changes --no-restore` | Passed |
| Dependency restore/audit metadata refresh | `dotnet restore GeoSurvey.sln --force-evaluate --disable-parallel --no-http-cache` | Passed after removing the restricted session's unusable proxy only for the authorized command; proxy environment restored afterward |
| Transitive NuGet vulnerability audit | `dotnet list GeoSurvey.sln package --vulnerable --include-transitive --no-restore` | All 21 projects reported no known vulnerable packages using refreshed advisory metadata |

Backend totals: ControlPlane SQL 276; Identity unit 202; Identity SQL 35; Organizations SQL 7; Organization unit 38; operator console unit 24; architecture 13; API integration 27; tenancy SQL 9. The API total includes the real SQL/HTTP bootstrap/enrollment/TOTP/RS256/recovery/refresh/logout acceptance test. Tenant tests use actual separate databases and restricted SQL principals.

The initial combined run had three new tenancy-test setup/assertion failures and one pre-existing bootstrap elapsed-time assertion failure. The tenancy tests were corrected to perform a real denied CREATE DATABASE operation and inspect migration history through the management principal; runtime permissions were not broadened. The untouched bootstrap test passed in the final combined rerun. Exact targeted evidence is in the [tenancy notes](../../development/tenant-provisioning-and-activation.md) and [authentication notes](../../development/phase-1-authentication.md).

Initial NuGet NU1900 metadata warnings were resolved by the successful fresh restore above; the final build had no warnings. Compatible dependency security pins include IdentityModel.JsonWebTokens 8.23.0, Microsoft.OpenApi 2.7.5 and Newtonsoft.Json 13.0.4. No unrelated framework/UI-library upgrade was made.

## Docker and browser checks

The dedicated API, worker and Angular/Nginx images built successfully. The full stack started with generated external private configuration; global migrations/roles and restricted bootstrap succeeded. HTTPS browser enrollment, sign-in, organization provisioning, activation and workspace paths were exercised in the first run; a later error-display check exposed a duplicate-alert UI defect. The frontend also corrected Angular critical-CSS loading to work with the strict CSP instead of relaxing it.

The final expanded fresh-environment command passed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Phase1Demo.ps1 -DemoRoot "<private-acceptance-directory>"
```

Result: **one complete Chromium scenario passed, zero failures, zero skipped checks**, 87.102 seconds, against the actual SQL/API/worker/Angular HTTPS stack. The retained [sanitized browser summary](evidence/browser-summary.json) records eleven checkpoints:

- Real TOTP enrollment and ten one-time recovery codes; fresh password/TOTP sign-in.
- HttpOnly/Secure/SameSite refresh cookies and no localStorage/sessionStorage credential persistence.
- Real provisioning, invitation, activation and database-backed workspace access for two organizations.
- Hostile `X-Tenant-ID`, `X-Organization-ID`, `X-Tenant-Database`, `organizationPublicId` and `database` inputs cannot change the signed-in account's tenant. Platform credentials on tenant setup and Organization credentials on platform administration both return HTTP 403.
- Same-email accounts have independent passwords/workspace identities; the other organization's password returns HTTP 401.
- Password plus unused recovery-code sign-in, cross-tab session-change detection, coordinated strict single-use refresh, and rejection of the consumed recovery code.

Earlier partial/diagnostic runs do not count as full acceptance. They exposed the duplicate-alert/CSP issues above and an HTTP 429 caused by the multi-user test exceeding the real 30/minute authentication budget. The harness now paces requests; the application limit was not weakened. Startup also now waits for exact `Healthy` through the web-to-API HTTPS path, trusting only the generated local certificate. This bounded readiness path passed on two fresh acceptance environments.

## Frontend and remaining evidence

The production Angular build passed at 285.81 kB initial bundle. The frontend owner reports 24 passing component/service tests in eight files (`npm test -- --watch=false`) and a passing production-dependency audit (`npm audit --omit=dev`, zero vulnerabilities). The additional DOM regressions verify one accessible error after a rejected password or consumed recovery code. Full development-tool audit findings remain documented in [frontend handover](frontend-flow.md); this report does not claim a clean all-dependency npm audit.

Root also executed `npm run format:check` and `npm run test:e2e:typecheck`: both passed after the final HTTP assertions. `npm run test:totp` passed both independent RFC-vector/malformed-input tests. Its first restricted-shell attempt was blocked by Windows `spawn EPERM`; the authorized rerun outside that restriction passed. That initial environment failure is not omitted or reported as an application pass.

## Clean source and packaging checks

A secret-scanned rehearsal archive was extracted into a new ignored directory without dependency/build output. From that extracted root, `dotnet restore GeoSurvey.sln --disable-parallel` and `dotnet build GeoSurvey.sln --configuration Release --no-restore -m:1` passed with zero warnings/errors. From its Angular directory, `npm ci --no-audit` installed the lockfile and `npm run build` passed at 285.81 kB. This proves clean extracted-source buildability using the installed tools and package caches; a separate hosted fresh-clone deployment is not claimed.

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package-Phase1Submission.ps1 -PrivateDemoRoot "<private-acceptance-directory>"` generated the rehearsal source archive. The script scanned selected source for the private environment's generated passwords/TOTP secret; the independent ZIP check verified all 416 source SHA-256 values and absence of keys, credentials, dependencies, build output and traces. The first Windows PowerShell 5.1 attempt required explicitly loading `System.IO.Compression`; packaging passed after that compatibility fix. The checksum-reader initially treated the JSON array as a nested array; the corrected independent reader passed without changing archive contents. Final regeneration includes this report and the retained safe browser summary; see the accompanying source manifest for final file hashes.

Final packaging passed with **417 source/documentation files plus the manifest (418 ZIP entries)**. Independent inspection verified every archived source checksum and the excluded-file rules. The archive contains the current uncommitted source, not only the last Git commit. Runtime secrets, private fixtures and generated build/dependency directories are excluded. Packaging does not perform a commit or push.

The final documentation review checked 504 local links, and `git -c core.safecrlf=false diff --check` passed. All four Phase 1 PowerShell scripts parsed without errors. These are documentation/diff/parser checks, not substitutes for the application results above. OpenAPI route coverage passed in the API integration suite; the 26-request Postman collection was parsed/reviewed with 18 empty placeholders and no saved responses, but was not separately executed. The CI job is configured; hosted GitHub Actions execution remains unverified.

Hosted GitHub Actions, production deployment, mobile/GIS/later-phase functionality, real production administrator credentials, production residency/retention policy, and cloud disaster-recovery verification were not performed. No commit/push was requested for this batch. The [known limitations](known-limitations.md) remain part of the handover.
