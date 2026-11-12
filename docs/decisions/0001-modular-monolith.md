# 0001: Modular monolith with Clean Architecture

Status: Accepted (existing master specification decision)

Recorded: 2026-11-11

## Context

GeoSurvey has several business responsibilities, including organizations, identity, projects, GIS, assignments, and surveys. The initial product needs clear ownership of those responsibilities without the operational cost of distributed services. The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md), ADR-001 and sections 3–4, selects a modular monolith with Clean Architecture and warns against generating dozens of empty projects.

## Decision

Start with one ASP.NET Core backend application. Introduce business modules incrementally in their approved phases. A module may begin with folders; split assemblies when there is a concrete boundary or build/test benefit.

Apply these dependency rules as module code is introduced:

- Domain contains business rules and must not depend on Application, Infrastructure, API, EF Core, or external services.
- Application implements use cases using Domain and abstractions.
- Infrastructure implements Application abstractions and may depend on Domain/Application.
- Presentation exposes use cases through endpoints. The API host is the composition root, registering implementations through Dependency Injection.
- Modules interact through explicit public application contracts or events. They must not manipulate another module's DbContext or internal tables, and dependencies must not form cycles.

The global identity/tenant registry and database-per-organization topology remain separate security requirements. Sharing an application deployment does not grant access across tenants. Tenant database selection and resource authorization must be enforced server-side when those features are implemented.

## Consequences

- Modules can evolve within one backend deployment and repository without requiring inter-service networking for every interaction.
- Logical boundaries require review and meaningful dependency checks. Folders alone do not enforce isolation; architecture tests or assembly boundaries should be introduced with applicable code.
- The API host currently contains infrastructure startup, health checks, and logging. Business modules and their layers have not yet been implemented; this record does not claim their boundaries have been tested.
- Modules share the backend deployment lifecycle and process resources. Any later extraction into separate services needs its own justification and approval.
- No package, interface, generic repository, or additional project is required merely to record this decision.

## Alternatives considered

Microservices would add deployment, communication, and consistency concerns before a demonstrated need. A backend with unrestricted cross-module access would undermine the selected ownership and dependency rules. Neither approach is the selected baseline.

## Related documentation

- [Repository structure and dependency boundaries](../architecture/repository-structure.md)
- [Decision index](README.md)
