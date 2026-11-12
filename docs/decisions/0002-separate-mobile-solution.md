# 0002: Separate mobile solution

Status: Accepted (approved Phase 0 mobile skeleton implementation)

Recorded: 2026-11-11

## Context

The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md), sections 3.1, 4, 5.2, and 17.3, places the ASP.NET Core backend, Angular web app, and MAUI mobile app in a monorepo. MAUI builds require platform-specific workloads and tooling. Backend development and its Ubuntu CI job should remain usable without installing Android or Apple build tooling.

## Decision

Keep the mobile application in the same repository, with a separate solution at `src/Mobile/GeoSurvey.Mobile.sln`. The root `GeoSurvey.sln` contains the backend API and its integration tests; it does not include the mobile project.

The approved initial mobile target is `net10.0-android`. Other platform template files remain in the project directory for later review, but their target frameworks are not enabled. Adding Windows, iOS, or Mac Catalyst builds requires a separately approved task and verification of the corresponding tooling.

Both solutions use the repository's root `global.json` and inherit `Directory.Build.props` settings. The mobile app calls future backend capabilities through approved public API contracts; separating solutions does not introduce a separate repository or authorize direct database access.

## Consequences

- Backend restore/build/test commands do not require MAUI workloads. Existing CI validates backend and frontend independently; it does not validate mobile builds.
- Mobile developers need compatible .NET, MAUI/Android workloads, Android SDK, and JDK tooling. Mobile build and formatting commands must name the mobile solution explicitly.
- Shared SDK and build-setting changes need consideration of both solutions. A successful backend build does not prove mobile compatibility.
- The Android Debug build and formatting check passed locally. No attached device or running emulator was available, so visual startup and device execution remain unverified.
- Mobile CI, additional targets, application distribution identity, signing, and device/UI testing remain future work. The initial static screen does not justify a test-only domain abstraction.

## Alternatives considered

Adding MAUI to the root solution would make broad solution commands involve platform workloads. Separate repositories would add coordination overhead without a current need. The separate solution keeps platform-specific builds explicit while preserving the approved monorepo.

## Verification commands

From the repository root:

```powershell
dotnet restore src/Mobile/GeoSurvey.Mobile.sln
dotnet build src/Mobile/GeoSurvey.Mobile.sln --no-restore
dotnet format src/Mobile/GeoSurvey.Mobile.sln --verify-no-changes --no-restore
```

These commands document the existing workflow; they were not rerun for this documentation-only task.

## Related documentation

- [Repository structure and dependency boundaries](../architecture/repository-structure.md)
- [Root setup instructions](../../README.md)
- [Decision index](README.md)
