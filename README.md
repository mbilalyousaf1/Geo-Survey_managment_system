# GeoSurvey

Multi-organization GIS field survey management platform, developed incrementally as a modular monolith.

## Current scope

Phase 0 is in progress. The repository contains a .NET 10 ASP.NET Core API host, a process liveness endpoint, Serilog JSON console logging, an API integration test, shared coding conventions, a GitHub Actions CI workflow for backend and frontend, an Angular 21 skeleton, a MAUI Android skeleton, and local API Docker configuration. Databases, business modules, and authentication are deferred to separately approved tasks.

## Prerequisites

- .NET SDK `10.0.301` or a later stable patch in the `10.0.3xx` feature band, as selected by `global.json`.
- Git.

Docker and Node.js are not required to build or run the current API skeleton.

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

This is static inspection: conditions are conservatively checked regardless of the current platform. It does not evaluate MSBuild imports or inspect NuGet, assembly, or namespace dependencies. Dynamic/property-based references are rejected explicitly and require a reviewed extension of the checker. Generated output and frontend dependency directories are skipped. Domain/Application boundary checks will be added when those layers exist.

Run from the repository root:

```powershell
dotnet test tests/Architecture/GeoSurvey.ArchitectureTests --configuration Release --no-build --no-restore
```

Restore and build the root solution first. The tests locate the checkout by walking upward from the test output directory to `GeoSurvey.sln`, so they require repository source files and are not standalone published tests. Root solution test commands, including existing backend CI, include these checks automatically without building MAUI.

## Continuous integration

[The CI workflow](.github/workflows/ci.yml) runs on pull requests, pushes to `main`, and manual dispatch. It checks out the repository, installs the SDK specified by `global.json`, restores dependencies, verifies formatting, builds in Release mode, and runs the tests using that Release build. GitHub executes steps in order and stops normal execution when a step fails.

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

MAUI, Docker, and formatting checks were not repeated in this task; their earlier results remain separate. An actual clean Git checkout and hosted CI run are still pending after separately approved commits/pushes. Phase 0 is not declared complete by this fresh-copy check.

## Architecture and working rules

- [Master specification](GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Development instructions](AGENTS.md)
- [Repository structure and dependency boundaries](docs/architecture/repository-structure.md)

Work on a feature branch and implement one approved small task at a time.
