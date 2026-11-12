# GeoSurvey development instructions

## Source of truth and scope

- Read `GeoSurvey_Master_Architecture_and_Codex_Spec.md` and this file before making changes.
- The master specification is the architecture and requirements baseline. Distinguish confirmed decisions, proposals, and open questions.
- Start with Phase 0 only. Do not implement business features until the foundation is approved and complete.
- Work on one small, reviewable task at a time. Never generate the entire application in one pass.
- Do not move to another task without the user's instructions.

## Pair programming workflow

1. Explain the concept, what is being built, and why it is needed.
2. Propose the design, affected files/projects/modules, database/API impact, security implications, dependencies, and relevant tests.
3. Show the implementation plan and wait for approval before substantial implementation.
4. Implement only the approved task. Explain any necessary change to unrelated code before making it.
5. Explain important generated classes, interfaces, methods, and patterns in simple language; use Roman Urdu when helpful.
6. Run relevant build, tests, formatting, and static checks. Report exact commands, failures, and skipped checks. Never claim an unexecuted check passed.
7. Explain the results and recommend the next small task, then wait for instructions.

## Architecture and implementation

- Follow Clean Architecture and Modular Monolith principles with strict module boundaries.
- Domain must not depend on Application, Infrastructure, API, EF Core, or external services.
- Application depends on Domain and abstractions. Infrastructure implements Application abstractions. API is the composition root and HTTP boundary.
- Modules communicate through explicit public application contracts or events. Never manipulate another module's internal tables or DbContext. Avoid circular dependencies.
- Introduce modules, projects, interfaces, and services only when their purpose is clear. Do not create dozens of empty projects or default generic repositories.
- Use services for simple operations and CQRS/MediatR for complex workflows where justified by the specification.
- Explain every added dependency. Do not introduce unnecessary packages, technologies, microservices, Kubernetes, GraphQL, or infrastructure without approval.
- Write maintainable, production-oriented code. Do not create fake implementations or placeholder success responses for unfinished features.
- Ask about open or ambiguous business rules before implementing affected behavior. Document unresolved decisions.
- Update relevant documentation and tests with changes; document architecture changes explicitly.

## Security and data integrity

- Preserve the global identity/tenant registry and database-per-organization architecture.
- Resolve tenant databases from trusted server-side registry and identity data. Never trust client-supplied tenant identifiers as authorization, database names, or connection strings.
- Enforce server-side tenant, permission, project, geographical assignment, and resource authorization where relevant. Never bypass authorization for convenience.
- Do not reuse EF contexts across tenants. Tenant isolation applies to requests, jobs, caches, SignalR groups, and files; test it where relevant.
- Keep secrets out of source control, Docker images, client responses, and logs. Keep configuration environment-specific; use development user-secrets or local environment configuration.
- Do not expose tenant database names, connection strings, internal integer IDs, or secrets to clients. Use public GUID `PublicId` values in public API routes.
- Persist timestamps in UTC.
- Preserve historical surveys, geometry versions, amendments, and audit events. Follow the specification's deletion/retention guardrails; do not invent final retention rules.
- Do not log credentials, tokens, unnecessary personal data, sensitive survey payloads, or unnecessary precise location history.
- Keep spatial calculations consistent with the CRS; do not use planar longitude/latitude calculations for accurate ground area or distance.

## Validation and definition of done

- Include appropriate unit and integration tests. Add architecture, security, end-to-end, and performance checks where relevant to the task.
- For a vertical slice, define behavior, enforce authorization, validate input, include required constraints/migrations, document the API, and complete client integration when in scope.
- Relevant builds and tests must pass before declaring implementation complete. Explain blocked or skipped checks rather than concealing them.
- Verify useful errors/logs without sensitive data leakage, tenant isolation where relevant, and documented local run commands.
- Do not require application builds or runtime tests for documentation-only changes; review their content and diff instead.

## Git and preservation of work

- Use feature branches. Never push directly to `main`.
- Never delete or overwrite existing work without approval.
- Review the Git diff before suggesting a commit. Keep commits small and meaningful.
- Do not commit or push unless requested or approved by the user.
