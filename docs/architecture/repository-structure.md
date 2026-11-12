# Repository structure and dependency boundaries

Status: initial solution, API host, liveness health check, structured console logging, and first API integration test implemented; the remaining Phase 0 work is pending separate approval.

The source of truth is [the master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md), especially sections 3, 4, 5, 16, and 17. This document records the planned organization; it does not change business requirements or resolve open decisions.

## Purpose

GeoSurvey will use a monorepo: backend, web, mobile, tests, and deployment instructions live in one repository. The backend starts as one ASP.NET Core modular monolith. Modules separate business responsibilities while remaining part of the same application deployment.

Clean Architecture keeps business rules independent of HTTP, databases, and other infrastructure. In simple terms: business rules define what must happen; infrastructure supplies the technical mechanisms.

The [architecture decision index](../decisions/README.md) records the reasoning and consequences of the [modular monolith baseline](../decisions/0001-modular-monolith.md) and the [separate mobile solution](../decisions/0002-separate-mobile-solution.md).

## Planned layout

Create each item only in its approved implementation task. This is a target layout, not a list of existing files.

```text
GeoSurvey.sln
AGENTS.md
README.md
GeoSurvey_Master_Architecture_and_Codex_Spec.md
src/
  Backend/
    GeoSurvey.Api/
    BuildingBlocks/                 # Shared capabilities only when needed
    Modules/                        # Business modules only when needed
    Workers/                        # Separate worker host only when justified
  Frontend/
    geosurvey-web/
  Mobile/
    GeoSurvey.Mobile.sln            # Independent platform-specific builds
    GeoSurvey.Mobile/
tests/
  Unit/
  Integration/
  Architecture/
  EndToEnd/
deploy/
  docker/
  azure/
  pipelines/
docs/
  architecture/
  decisions/
  api/
  operations/
scripts/
docker-compose.yml                 # Add when local dependencies are needed
```

- `GeoSurvey.Api` hosts HTTP endpoints and composes dependencies through Dependency Injection.
- `Modules` will contain responsibilities such as Organizations, Identity, Projects, GIS, Assignments, and Surveys. They are deferred until their approved phases.
- `BuildingBlocks` is for justified shared capabilities, not a place to move module-specific business logic.
- The Angular application will use feature-based organization. A UI component library remains undecided.
- The MAUI application requires verification of the installed SDK, workloads, and target platform tooling before scaffolding/building.
- Test folders distinguish behavior tests, real infrastructure/API integration checks, dependency rules, and complete user workflows. Add test projects when they have meaningful checks to run.
- Deployment files, CI, Docker dependencies, and scripts will be introduced incrementally with exact run instructions.

## Module organization

A module can begin with folders. Separate assemblies are appropriate when they provide a concrete boundary or build/test benefit; do not generate every module as several empty projects.

```text
Modules/<ModuleName>/
  Domain/
  Application/
  Infrastructure/
  Presentation/
```

| Area | Responsibility | Dependency rule |
| --- | --- | --- |
| Domain | Entities, value objects, and business rules | No dependency on Application, Infrastructure, API, EF Core, or external services |
| Application | Use cases and required abstractions | Depends on Domain and abstractions |
| Infrastructure | Persistence and external service implementations | Implements Application abstractions; depends on Application/Domain |
| Presentation | HTTP endpoints and request/response mapping | Calls application use cases; keeps business rules out of endpoints |
| API host | Startup, HTTP hosting, and dependency registration | Composition root; connects implementations to abstractions |

An interface defines a capability a use case needs. An infrastructure class implements that capability. Dependency Injection connects the two at startup. Add an interface when this separation has a concrete purpose, rather than wrapping every class automatically.

Modules may interact through explicit public application contracts or events. They must not access another module's internal tables or DbContext, and must not form circular dependencies. Dependency checks will be added with the relevant code.

## Tenant security boundary

The planned database topology remains unchanged: a global control-plane database for identity and tenant registry, with an independent operational database for each organization. Module-based SQL schemas organize data within an organization's database; they do not replace database-per-tenant isolation.

Database selection must come from trusted server-side identity and registry data. Authorization must establish permitted organization membership and resource scope. A client-provided identifier alone never grants tenant access. No database, identity, or tenant resolution implementation is part of this documentation task.

## Phase 0 sequencing

Each step requires its own small task explanation and approval:

1. Record development instructions and repository structure (complete).
2. Propose and create the solution and minimal API host, with exact SDK configuration and run instructions (implemented: `GeoSurvey.sln`, `GeoSurvey.Api`, and `global.json`; see the root README).
3. Add health checks and structured logging with meaningful verification (complete: `/health/live`, Serilog compact JSON console logs, and a liveness integration test).
4. Add dependency checks as supported by the skeleton (implemented: static project reference checks prevent production-to-test and backend/mobile references, reject unknown targets, and detect cycles; checks for internal module layers remain deferred until those layers exist).
5. Verify tool compatibility and create the Angular skeleton (implemented: Angular 21 standalone app with strict TypeScript, routing configuration, and a root component test; frontend CI configuration implemented, hosted run pending).
6. Verify target tooling and create the MAUI skeleton (implemented: Android-only `GeoSurvey.Mobile` in the separate `src/Mobile/GeoSurvey.Mobile.sln`; Debug Android build verified, device startup pending because no device/emulator was attached).
7. Add relevant Docker support and a basic CI build/test pipeline (backend/frontend CI and API Docker configuration implemented; Compose validation, Linux image build, non-root container startup, and HTTP liveness verified; hosted CI and clean-checkout verification pending).

The precise scope of each step will be proposed before implementation. Phase 0 finishes when a clean checkout can build and run locally with documented commands. It includes no authentication, tenant provisioning, GIS, or survey features.

Fresh source verification on 2026-11-12 passed: a temporary copy of Git-listed, non-ignored files restored and built the backend in Release mode with 8 passing tests; the copied API returned `200 Healthy`. Angular `npm ci`, production build, and its 1 component test also passed. Existing shared package caches and installed tooling were available. This was not an actual Git checkout (the repository had no commits), and does not replace hosted CI or the clean-checkout exit criterion. MAUI, Docker, and formatting were not repeated in this task. See the root README for exact commands and unresolved dependency findings.

## Inspection baseline

Observed on 2026-11-11:

- The repository initially contained only the untracked master specification and Git metadata; no solution, application, tests, or `AGENTS.md` existed.
- Git reported no commits on `main` and an unavailable tracked upstream reference. Remote availability was not checked over the network.
- Installed .NET SDKs: `9.0.312` and `10.0.301`; no `global.json` existed.
- .NET reported Android, iOS, Mac Catalyst, and Windows MAUI workloads. Platform builds and external tooling were not verified.
- Node.js `24.14.1`, npm `11.11.0`, and Git `2.53.0.windows.2` were available.
- Docker CLI `29.6.2` was available, but `docker info` could not reach the Docker Desktop Linux engine.

These are local observations, not pinned dependency versions or compatibility guarantees. Select and verify framework/package versions in the affected scaffolding task. Resolve the specification's open business decisions only before their affected features; they do not block this documentation task.
