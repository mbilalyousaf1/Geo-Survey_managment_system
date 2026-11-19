# GeoSurvey

Multi-organization GIS field survey management platform, developed incrementally as a modular monolith.

## Current scope

Phase 0 is accepted. The approved Phase 1 local Docker delivery is complete: Platform password/MFA authentication, signed sessions and refresh/logout, organization creation, durable provisioning of a separate database per organization, administrator activation, trusted tenant resolution and the Angular workflow. The final fresh Docker/browser scenario passed all eleven checkpoints with nothing skipped. GIS/survey features, general RBAC and mobile integration remain in later phases.

Start with the [Phase 1 submission guide](docs/submission/phase-1/README.md), [local Docker runbook](docs/submission/phase-1/runbook.md), [exact validation results](docs/submission/phase-1/validation-results.md), and [known limitations](docs/submission/phase-1/known-limitations.md). The current backend verification passed 631 tests and a Release build with zero warnings/errors. No production deployment or real administrator credentials are included.

The sections below retain the original foundation setup/history. For the complete Phase 1 workflow, use the Docker runbook above; historical skeleton-only descriptions do not describe the current integrated application.

## Prerequisites

- .NET SDK `10.0.301` or a later stable patch in the `10.0.3xx` feature band, as selected by `global.json`.
- Git.

Docker Desktop with Linux containers and the Node/npm versions in the web package are required for the Phase 1 Docker/browser workflow. The .NET-only build commands below also support foundation development.

## Build

Run from the repository root:

```powershell
dotnet --version
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --no-restore
```

## Run locally

```powershell
dotnet run --project src/Backend/GeoSurvey.Api --no-build --launch-profile http
```

The development host listens at `http://localhost:5121`. Requesting `/` returns HTTP `404`; the liveness endpoint is `/health/live`. In another PowerShell terminal:

```powershell
(Invoke-WebRequest http://localhost:5121/health/live).Content
```

The response is HTTP `200` with body `Healthy`. Alternatively, use `curl.exe -i http://localhost:5121/health/live` on Windows. Stop the host with `Ctrl+C`.

For local HTTPS, trust the development certificate once, then use the HTTPS profile:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/Backend/GeoSurvey.Api --no-build --launch-profile https
```

The HTTPS address is `https://localhost:7291`. The HTTP profile is for local development only. Deployed environments must use HTTPS and environment-specific configuration; production deployment is not configured in this skeleton.

## Angular web skeleton

The standalone Angular app lives in `src/Frontend/geosurvey-web`. It uses routing, strict TypeScript/template checks, and plain CSS. It currently displays a GeoSurvey heading with no backend integration or business features. No UI library has been selected.

Verified local tooling: Node.js `24.14.1`, npm `11.11.0`, and Angular CLI `21.2.29`. Angular 21 supports Node `^20.19.0 || ^22.12.0 || ^24.0.0`; see the [official compatibility table](https://angular.dev/reference/versions). The lockfile records exact installed dependencies. Use `npm ci` for reproducible installation, without a global CLI install.

From the repository root:

```powershell
cd src/Frontend/geosurvey-web
npm ci
npm run build
npm test -- --watch=false
npm start -- --host 127.0.0.1
```

On Windows, use `npm.cmd` if PowerShell blocks `npm.ps1`. Open `http://127.0.0.1:4200` and stop the development server with `Ctrl+C`. The build uses the production configuration by default and writes to `dist/geosurvey-web`. The component test uses the CLI's Vitest runner with jsdom; no separate browser installation is needed for this test.

`src/main.ts` bootstraps the root `App` component using `app.config.ts`. The configuration registers the router with the empty `app.routes.ts` route list. `RouterOutlet` provides the location for future route components. The heading test compiles the actual root template and verifies its visible title.

The CLI generates TypeScript, RxJS, Angular build tooling, Vitest/jsdom, and Prettier dependencies. These support compilation, Angular runtime behavior, tests, and the generated formatting configuration. CLI analytics are disabled. The CI workflow validates frontend formatting, production build, and component tests in a separate job.

Dependency review on 2026-11-11: `npm audit` reported 16 moderate findings in the CLI development dependency chain involving `glob` and `minimatch`. `npm audit --omit=dev` reported zero findings. The suggested automatic fix changes the Angular CLI major version, so it was not applied. These development-tool findings remain unresolved and need a compatible upstream update or a separately reviewed dependency change; do not run `npm audit fix --force` blindly.

To verify frontend formatting, run `npm exec -- prettier --check src angular.json package.json tsconfig.json tsconfig.app.json tsconfig.spec.json .prettierrc` from the Angular directory. Its generated local `.editorconfig` uses two-space indentation for frontend files.

## MAUI Android skeleton

The mobile app lives in `src/Mobile/GeoSurvey.Mobile` and targets `net10.0-android` only. Its separate solution, `src/Mobile/GeoSurvey.Mobile.sln`, keeps mobile workloads out of the backend solution and Ubuntu CI jobs. Windows, iOS, and Mac Catalyst template source files remain available for future separately approved targets; they are not currently built.

Prerequisites: the SDK selected by root `global.json`, compatible MAUI/Android workloads, Android SDK, and a compatible JDK. This machine reports MAUI `10.0.20`, Android workload `36.1.43`, and installed Android platforms `36`/`36.1`. Workloads installed through Visual Studio can appear as `android`, `maui-windows`, `ios`, and `maccatalyst` rather than a single `maui` entry. See [Microsoft installation instructions](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0).

From the repository root:

```powershell
dotnet restore src/Mobile/GeoSurvey.Mobile.sln
dotnet build src/Mobile/GeoSurvey.Mobile.sln --no-restore
dotnet format src/Mobile/GeoSurvey.Mobile.sln --verify-no-changes --no-restore
```

The Debug build creates Android packages under `src/Mobile/GeoSurvey.Mobile/bin/Debug/net10.0-android/`. Do not commit packages, signing keys, or local SDK/JDK paths. If auto-discovery fails, provide local `AndroidSdkDirectory` and `JavaSdkDirectory` MSBuild properties or configure the IDE; do not hardcode machine paths in the project.

The verified build auto-discovered JDK `21.0.8` and Android build tools `36.0.0`, despite Java 17 being the terminal default. Restore, Debug build, and formatting verification passed. The generated signed APK uses development signing, not a production release identity.

`MauiProgram.CreateMauiApp()` configures the MAUI application, fonts, and Debug logging. `App` creates a window containing `AppShell`, which hosts `MainPage`. XAML defines the screen layout; `MainPage.xaml.cs` calls `InitializeComponent()` to initialize the generated controls. The screen contains only a GeoSurvey heading; there is no counter, authentication, GPS, camera, offline storage, or API integration.

`Microsoft.Maui.Controls` supplies the UI framework using the installed workload's `MauiVersion`; `Microsoft.Extensions.Logging.Debug` supplies template Debug logging. Nullable analysis, implicit imports, and analyzer settings are inherited from root `Directory.Build.props`. The generated `com.companyname.geosurvey.mobile` application identifier is a development template value; production package identity remains undecided and must be selected before distribution.

Run `adb devices -l` to inspect connected devices. No device or running emulator was attached during this task, so visual startup and device execution were not verified. To run later, select an Android emulator or device in Visual Studio and use `GeoSurvey.Mobile` as the startup project. No artificial unit test is added for the static heading. Mobile CI and device/UI tests are deferred.

## Backend host startup

`Program.cs` creates a `WebApplicationBuilder`, registers Serilog and built-in health checks, then builds the application. Request logging middleware wraps endpoint execution and records completion. `MapHealthChecks` maps the liveness route; `Run()` starts the HTTP server. The public partial `Program` declaration allows integration tests to locate the generated entry point.

`GeoSurvey.Api.csproj` uses the ASP.NET Core Web SDK, targets `net10.0`, enables nullable reference analysis, and enables implicit common imports. `Serilog.AspNetCore` integrates Serilog with ASP.NET Core; `Serilog.Formatting.Compact` supplies the compact JSON console formatter. Health checks are included in the ASP.NET Core shared framework and require no additional package.

The solution groups projects; `global.json` selects the SDK used to build them. The target framework selects the runtime/API surface for the application. These are different responsibilities.

## Configuration and secrets

Committed `appsettings` files contain Serilog minimum levels and host configuration only. `Serilog:MinimumLevel:Default` controls application logging; the `Microsoft.AspNetCore` override reduces framework request noise. The launch settings select the Development environment and local addresses. Keep secrets in development user-secrets or environment variables; never commit credentials or production configuration.

## Liveness and logging

`GET /health/live` is an unauthenticated infrastructure endpoint returning only the built-in health status, without dependency details. With no registered dependency checks, it confirms that the running process can serve the request. It does not establish database connectivity, tenant readiness, or business correctness. Dependency readiness checks must be designed separately when dependencies exist. Health responses disable caching.

Console logs use compact JSON with UTC timestamps (`@t`), message templates (`@mt`), and named properties. Serilog request completion events include `RequestMethod`, `RequestPath`, `StatusCode`, and `Elapsed` (milliseconds). Default request logging excludes query strings, headers, and bodies; no additional request enrichment is configured. Paths are logged, so future endpoint designs must keep secrets out of paths. Application exception messages and any future custom log properties also need review for sensitive data.

## Coding conventions and shared build settings

The root `.editorconfig` defines UTF-8 text, a final newline, trailing-whitespace cleanup, and four-space indentation for C#. Markdown preserves trailing spaces because they can represent intentional line breaks. Existing line endings are preserved; this task does not normalize the repository's line endings.

MSBuild automatically imports the nearest `Directory.Build.props` found above each project. Our root file supplies these settings to both the API and integration test project:

- `Nullable`: enables compiler analysis of possible null-reference errors.
- `ImplicitUsings`: adds common namespace imports appropriate to each project's SDK.
- `EnableNETAnalyzers`: enables the SDK's built-in .NET code-quality analyzers.
- `EnforceCodeStyleInBuild`: enables build-time evaluation of supported code-style diagnostics, according to their configured severity. It does not make every formatting preference a build error.
- `TreatWarningsAsErrors`: remains `false`; warnings are reported without failing the build globally.

No analyzer packages are added. Analyzer defaults follow the selected SDK and target framework. Framework targets and package references remain in each `.csproj`, while shared compiler settings live in one place. A project can override inherited properties later when a documented need exists.

Use the formatting verification command below to check supported formatting and style rules. To apply formatting intentionally, run `dotnet format GeoSurvey.sln --no-restore`, then review the diff.

## Verification commands

```powershell
dotnet test GeoSurvey.sln --no-build --no-restore
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
```

Run restore/build first. `LivenessTests` uses `WebApplicationFactory<Program>` to start the real API pipeline in an in-memory server, then checks HTTP `200`, plain-text `Healthy`, and disabled caching. The test project references the API, `Microsoft.AspNetCore.Mvc.Testing` (matched to the installed ASP.NET Core runtime), xUnit, its Visual Studio adapter, and the .NET test SDK. There is no domain logic requiring unit tests yet.

Implementation references: [Serilog ASP.NET Core](https://github.com/serilog/serilog-aspnetcore) and [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).

## Project dependency checks

`tests/Architecture/GeoSurvey.ArchitectureTests` inspects literal `ProjectReference` entries in `.csproj` files under `src` and `tests`. Production projects cannot reference test projects; backend and mobile projects cannot reference each other; references cannot form cycles. Missing or undiscovered targets fail validation as well. Test projects may reference production code.

`ProjectDependencies` reads the project graph using built-in XML APIs, normalizes slash styles, and traverses dependencies to detect cycles. `ProjectDependencyTests` checks the repository and uses small in-memory graphs to prove forbidden references and cycles fail while permitted references pass. Existing xUnit/test SDK versions are reused; no architecture library or application reference is added.

Backend projects whose filenames end in `.Domain.csproj` must declare no `PackageReference` or `ProjectReference` elements. The repository test explicitly requires the Organizations Domain project to be discovered, preventing an empty scan from passing. Tests also verify rejection of conditional references and acceptance of the standard framework SDK without added dependencies. Any future shared Domain dependency requires a reviewed rule change.

This is static inspection: conditions are conservatively checked regardless of the current platform. Domain reference declarations are rejected even when expressed as Update/Remove entries. The checker does not evaluate MSBuild imports, SDK resolution, resolved NuGet dependencies, direct assembly/framework references, or C# namespaces; it does not prove complete assembly isolation. Dynamic/property-based project paths are rejected explicitly and require a reviewed extension of the checker. Generated output and frontend dependency directories are skipped. Application-layer rules will be added when that layer exists.

Run from the repository root:

```powershell
dotnet test tests/Architecture/GeoSurvey.ArchitectureTests --configuration Release --no-build --no-restore
```

Restore and build the root solution first. The tests locate the checkout by walking upward from the test output directory to `GeoSurvey.sln`, so they require repository source files and are not standalone published tests. Root solution test commands, including existing backend CI, include these checks automatically without building MAUI.

## Continuous integration

[The CI workflow](.github/workflows/ci.yml) runs on pull requests, pushes to `main` or `chore/phase0-foundation`, and manual dispatch. It checks out the repository, installs the SDK specified by `global.json`, restores dependencies, verifies formatting, builds in Release mode, and runs the tests using that Release build. GitHub executes steps in order and stops normal execution when a step fails.

Backend and frontend jobs run independently on Ubuntu hosted runners, each with a 15-minute timeout. The workflow grants its GitHub token only read access to repository contents. Checkout does not persist credentials. No custom secrets are required, and the workflow does not publish packages, deploy, or push commits. A push to `main` trigger validates merged changes; development work still belongs on feature branches.

To run the equivalent validation locally, from the repository root:

```powershell
dotnet restore GeoSurvey.sln
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test GeoSurvey.sln --configuration Release --no-build --no-restore
```

The independent `frontend` job installs Node.js `24.14.1` with `actions/setup-node`, then runs the following commands in `src/Frontend/geosurvey-web`:

```powershell
npm ci
npm exec -- prettier --check src angular.json package.json tsconfig.json tsconfig.app.json tsconfig.spec.json .prettierrc
npm run build
npm test -- --watch=false
```

`npm ci` installs from `package-lock.json` and fails if it is inconsistent with `package.json`. Automatic package-manager caching is disabled to keep this initial job simple. Each job can report its own failure independently. No new application packages or secrets are needed. The documented development dependency audit findings remain unresolved; this workflow does not add an audit gate.

Local validation on Windows does not establish that the Ubuntu hosted run has passed. The actual workflow must reach GitHub through a separately approved commit/push and pull request before the hosted run can be verified. Manual dispatch requires the workflow to exist on the default branch. Repository branch protection and required checks are not configured by this file.

Official references: [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet), [setup-node](https://github.com/actions/setup-node), and [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).

## Local API container

Prerequisites: Docker Desktop with a working Linux container engine and Docker Compose. A host .NET SDK is not required for the Docker build; its SDK stage builds the API. From the repository root:

```powershell
docker compose config --quiet
docker compose up --build --detach api
curl.exe --fail --show-error http://127.0.0.1:5080/health/live
docker compose logs api
docker compose down
```

On Linux/macOS, use `curl` instead of `curl.exe`. The health response should be `200 Healthy`. `/` still returns `404`. The host port is bound to loopback only; local HTTP is for development. Production ingress and HTTPS termination require a separately reviewed deployment configuration.

`deploy/docker/GeoSurvey.Api.Dockerfile` uses two stages: SDK `10.0.301-noble` restores/publishes the API in Release mode; ASP.NET runtime `10.0-noble` runs only the published output. The SDK version matches `global.json`; update them together. The runtime tag follows .NET 10 patches and is not digest-pinned. Both images come from Microsoft's official registry. Both image tags resolved successfully during the verified local build.

`USER $APP_UID` selects the official image's non-root application user. The app listens on container port `8080`, mapped to host `127.0.0.1:5080`. Compose uses the Production environment to avoid development exception details, and disables privilege escalation. Structured logs go to container stdout. No secrets, development certificates, database, or additional services are configured.

The root `.dockerignore` limits build context to API inputs and shared build settings, excluding local output, launch settings, development overrides, and common credential files. Future configuration still needs secret review; filename exclusions alone cannot identify every secret. The Dockerfile copies only API inputs, so Angular and MAUI tooling are not required. No internal container health probe is configured; the command above checks the existing HTTP liveness endpoint from the host.

Verification on 2026-11-11: after the user installed WSL and started Docker Desktop, the Linux engine reported `29.6.2` and WSL reported `3.0.5.0`. `docker compose config --quiet`, `docker compose build api`, and `docker compose up --detach --no-build api` passed. `docker compose ps` confirmed the loopback port mapping, and `docker compose exec -T api id` confirmed non-root `uid=1654(app)`. Startup logs confirmed Production environment and JSON output. `curl.exe --fail --show-error --include http://127.0.0.1:5080/health/live` returned `200 Healthy` with caching disabled. This resolves the earlier Docker Desktop startup blocker. The container was left running for local use; use `docker compose down` when finished.

References: [ASP.NET Core containers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images?view=aspnetcore-10.0) and [official image tag policies](https://github.com/dotnet/dotnet-docker/blob/main/documentation/supported-tags.md).

## Fresh source verification

On 2026-11-12, 81 non-ignored source files listed by Git were copied into a new temporary directory without `.git`, `bin`, `obj`, `node_modules`, or Angular build/cache output. The original source files were left intact. Verification used the existing Windows machine, installed SDK/Node tooling, and normal shared package caches; it was not a clean machine or Git checkout. The repository still had no commits.

Commands run from the fresh source root:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test GeoSurvey.sln --configuration Release --no-build --no-restore
dotnet run --project src/Backend/GeoSurvey.Api --configuration Release --no-build --launch-profile http
```

Restore and Release build passed with zero warnings/errors. All 8 backend tests passed (7 architecture, 1 integration). While the copied API host was running, `curl.exe --fail --show-error --include http://localhost:5121/health/live` returned `200 Healthy`. The verification host was stopped afterward.

From the copied `src/Frontend/geosurvey-web` directory:

```powershell
npm.cmd ci
npm.cmd run build
npm.cmd test -- --watch=false
```

Installation and production build passed, and 1 component test passed. Installation reported the previously documented 16 moderate dependency findings; they remain unresolved. No dependency versions were changed by this verification.

MAUI, Docker, and formatting checks were not repeated in this task; their earlier results remain separate. At the time of this fresh-copy check, an actual clean Git checkout and hosted CI run were pending. The later clean-checkout result is recorded below. Phase 0 was not declared complete by this fresh-copy check.

## Clean Git checkout verification

On 2026-11-12, `git clone --no-hardlinks --branch chore/phase0-foundation --single-branch . <temporary-directory>` created an independent local clone of commit `12ef425`. Only committed files were available initially; no original build output or `node_modules` was copied. This verifies the local committed source, not a fresh download from GitHub or a clean machine. Installed tooling and shared NuGet/npm caches remained available.

The following commands passed in the clone:

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test GeoSurvey.sln --configuration Release --no-build --no-restore
dotnet run --project src/Backend/GeoSurvey.Api --configuration Release --no-build --launch-profile http
```

The build had zero warnings/errors; 7 architecture tests and 1 API integration test passed. While the cloned host was running, `curl.exe --fail --show-error --include http://localhost:5121/health/live` returned `200 Healthy`. The host was stopped after verification.

From the clone's `src/Frontend/geosurvey-web` directory, `npm.cmd ci`, `npm.cmd run build`, and `npm.cmd test -- --watch=false` passed; 1 component test passed. The existing 16 moderate development-dependency findings were reported again. MAUI, Docker, and formatting were not repeated. At the time of this check, hosted GitHub CI, mobile device execution, and resolution of dependency findings remained outstanding. The later hosted CI result is recorded below; mobile device execution and dependency findings remain unresolved.

## Phase 0 acceptance review

Review date: 2026-11-12. The deliverables in master specification sections 16 (Phase 0) and 17.3 are implemented. Recommendation: ready for user acceptance with the unresolved items below explicitly acknowledged. This review does not itself approve the foundation or authorize Phase 1.

| Required deliverable | Evidence |
| --- | --- |
| Monorepo and solution/project skeletons | Root backend solution, Angular app, and separate Android MAUI solution |
| Coding conventions | `.editorconfig`, `Directory.Build.props`, and successful formatting checks |
| Docker local development | API image built, non-root container started, loopback health endpoint returned `200 Healthy` |
| Basic CI pipeline | [Hosted run 43205939746](https://github.com/mbilalyousaf1/Geo-Survey_managment_system/actions/runs/43205939746), commit `e5072b4`: backend and frontend jobs succeeded |
| Health checks and structured logging | Built-in liveness endpoint, integration test, and observed Serilog JSON console output |
| Architecture decision records | [ADR index](docs/decisions/README.md) records the modular monolith and separate mobile solution decisions |
| Initial architecture tests/dependency rules | 7 architecture checks passed; inspect project references without building MAUI |
| Setup documentation and clean checkout | Local Git clone of `12ef425` restored/built the backend, passed 8 backend tests, served `200 Healthy`, and installed/built/tested Angular successfully |
| No premature business features | Authentication, tenant provisioning, GIS workflows, and survey features remain unimplemented |

The hosted CI result was rechecked read-only for this review. Both jobs completed successfully, verifying backend restore, formatting, Release build and tests, plus frontend clean installation, formatting, production build and tests on Ubuntu. Local application builds/tests, Docker, and MAUI builds were not repeated for this documentation-only review.

Remaining limitations for the user's acceptance decision:

- The last recorded npm audit reports 16 moderate development-tool dependency findings. They are unresolved; the earlier production-only audit reported zero findings. No new audit or forced dependency upgrade was performed in this review. Acceptance must not be interpreted as remediation or production security approval.
- Android Debug build succeeded, but device/emulator execution remains unverified. Mobile builds were not repeated from the clean clone, and mobile CI is not configured. Apple and Windows targets remain disabled.
- The clean checkout used the existing Windows tooling and shared caches. It was a local Git clone, not a clean-machine test. Hosted CI separately verified backend/frontend on Ubuntu.
- Repository default branch is currently `chore/phase0-foundation`; the workflow explicitly includes this branch's pushes. Branch strategy and required-check protection are not configured by this review.
- The runtime image tag is not digest-pinned. Deployment, release signing, full security/performance testing, and production operations remain outside this foundation task.

No required Phase 0 skeleton deliverable was found missing. Unresolved dependency findings should be triaged in a separately approved small task, and mobile device validation remains outstanding. Obtain the user's explicit foundation acceptance before planning implementation in the next phase.

## Architecture and working rules

The [bootstrap console and operator runbook](docs/development/bootstrap-console.md) now provide masked interactive first-administrator creation through the tested service. It requires a dedicated operator connection and existing reviewed database setup. MFA enrollment/login and later Phase 1 workflows remain pending; no real administrator was created during implementation.

The [transactional bootstrap creation service](docs/development/bootstrap-creation-service.md) and reviewed role-setup script now exist with SQL Server concurrency, rollback, timeout, and restricted-permission tests. The service is not registered in the API; no console or real administrator creation has been performed.

[Permanent bootstrap-completion storage](docs/development/bootstrap-completion-storage.md) now adds the singleton table and a tested second migration. Atomic administrator creation and the operator command remain pending; the table is not seeded during migration.

The [bootstrap password base components](docs/development/bootstrap-password-components.md) and [verified local blocklist](docs/development/local-password-blocklist.md) now implement accepted normalization/length, hashing settings, and corpus checks. Account creation and command/API integration remain pending.

API startup now requires valid `ConnectionStrings:GlobalControlPlane` configuration with explicit Server and Database. Supply it through Development user-secrets or `ConnectionStrings__GlobalControlPlane`; see [API setup, scoped registration, and Docker instructions](docs/development/api-control-plane-configuration.md). Startup validates configuration without connecting or migrating; `/health/live` remains independent of database availability. Earlier Phase 0 run examples below/above describe their historical verification environment and now require this configuration.

The [initial global migration](docs/development/initial-global-migration.md) and review SQL are generated using repository-local EF tooling. [Migration application tests](docs/development/migration-application-tests.md) now apply and reapply the migration only in a fresh disposable SQL Server database. The saved review SQL is not executed; other model-only fixtures still use `EnsureCreated`.

The [global control-plane context](docs/development/control-plane-persistence-tests.md) now composes the module mappings and enforces the account-to-organization FK. It is covered by the same isolated backend SQL Server runner. Application outside disposable tests and API registration remain pending.

Identity account/supporting mappings are verified by SQL Server tests in the same backend runner. See [Identity persistence implementation and test results](docs/development/identity-persistence-tests.md), including the explicitly deferred Organization FK and production global context.

Organization mapping has SQL Server integration tests. After backend restore/build, run `./scripts/Test-Backend.ps1` with Docker's Linux engine running to execute the complete backend suite. A plain solution test command needs the isolated connection supplied by that script. See [Organization persistence and test instructions](docs/development/organization-persistence-tests.md) for dependencies, cleanup, and coverage.

- [Master specification](GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Development instructions](AGENTS.md)
- [Repository structure and dependency boundaries](docs/architecture/repository-structure.md)

Work on a feature branch and implement one approved small task at a time.

The [restricted MFA challenge storage/service](docs/development/mfa-challenges.md) is implemented with hash-only ten-minute tokens and SQL concurrency tests. Latest local backend verification: 257 passing tests. Authenticator enrollment and administrator login remain pending.

The [account-bound TOTP secret protector](docs/development/totp-secret-protection.md) is implemented and tested. Persistent certificate-protected host keys and authenticator enrollment are the next integration work; the API does not yet enable this component.

API startup now requires [persistent certificate-protected Data Protection configuration](docs/development/data-protection-host.md), including an external key-ring directory and mounted PFX/password. Configure these alongside the control-plane connection before running the API; no real key material is committed.

[Protected pending authenticator enrollment](docs/development/pending-totp-enrollment.md) is implemented as an internal service and fourth migration. It validates enrollment challenges and stores ciphertext without enabling MFA; authenticator confirmation and login are still pending.

The [TOTP primitive](docs/development/totp-primitive.md) now generates canonical authenticator secrets and verifies the approved six-digit codes. Attempt/replay controls and atomic enrollment confirmation remain pending before API integration.

[Persistent MFA attempt/replay controls](docs/development/mfa-attempt-replay-controls.md) now enforce the approved five-failure/15-minute budget and account-level accepted-step history. Full local backend verification: 370 tests passed. Confirmed enrollment and public authentication still require integration.

[Internal atomic MFA enrollment confirmation](docs/development/mfa-enrollment-confirmation.md) verifies real authenticator codes, enables MFA transactionally, and returns ten initial recovery codes once while storing only their hashes. That slice passed 409 backend tests. Public enrollment/login and recovery-code consumption remain pending.

[Internal Platform Administrator password entry](docs/development/platform-password-entry.md) verifies scoped credentials and applies the approved password-lockout policy before issuing a restricted enrollment/verification challenge. That slice passed 453 backend tests; public/session integration remains pending.

[Internal confirmed-factor verification](docs/development/platform-mfa-verification.md) resolves the account from its stored verification token, verifies confirmed TOTP, and applies attempt/replay controls in a caller-owned transaction. That slice passed 487 backend tests. Its provisional subject result requires later atomic session issuance before authentication is complete.

[Session and refresh-token storage](docs/development/session-refresh-token-storage.md) adds account-bound token families and digest-only refresh records, UTC/integrity constraints, retained consumption history, and one unconsumed token per family. That slice passed 541 backend tests. The storage foundation alone does not enable public login.

[Internal Platform Administrator session start](docs/development/platform-session-start.md) now commits actual MFA verification with the initial family/refresh hash and returns the raw refresh ticket only after commit. The accepted Platform limits are a five-minute future access-token maximum, thirty-minute refresh inactivity window, and eight-hour absolute session lifetime. The final full local backend run passed 595 tests, zero failures/skips, after correcting one new test fixture; Release builds and formatting/analyzers passed. JWT signing/validation, refresh/revocation, public authentication/authorization, and client delivery remain separate work.
