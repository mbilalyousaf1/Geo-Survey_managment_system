# Organization mapping and control-plane context design

Implementation update, 2026-11-16: `GeoSurvey.ControlPlane.Infrastructure` now contains the production global context and SQL Server options helper, explicitly composed module mappings, and account-to-organization NO ACTION FK. API registration, migrations, and provisioning are not implemented. See [global-context explanation and SQL Server tests](../development/control-plane-persistence-tests.md). Earlier statements about global composition being pending describe the proposal stage.

Status: Organization mapping/materialization accepted in [record 0017](../decisions/0017-organization-mapping-and-materialization.md); project/context names, dependency direction, explicit mapping/FK composition, and migration-history location accepted in [record 0018](../decisions/0018-global-persistence-composition.md). Extra constraints and detailed implementation remain proposed and require separate approval.

Prepared: 2026-11-13

Implementation update, 2026-11-13: the separately approved first slice adds Organization mapping, the persisted-value constructor, and SQL Server tests. See [implementation and test instructions](../development/organization-persistence-tests.md). Statements below describing the earlier documentation-only proposal are historical; global context, Identity mappings, migrations, and runtime wiring remain unimplemented.

## Approved baseline and scope

[Record 0015](../decisions/0015-control-plane-persistence-ownership.md) accepts module-owned mappings composed into one global DbContext and migration history, with organizations stored in `organization.Organizations`. [Record 0016](../decisions/0016-minimal-organization-entity.md) defines the minimal Organization entity. It is now implemented in Organizations Domain; IdentityAccount and AccountEmail exist in Identity Infrastructure. No EF Core provider, mapping, context, migration, or persisted organization exists yet.

The [account mapping design](identity-account-ef-mapping-design.md) remains applicable. This document proposes organization mappings and a concrete composition structure; it does not select additional business fields or authentication workflows.

## Organization mapping

| Property | Proposed mapping | Status/basis |
| --- | --- | --- |
| Table | `organization.Organizations` | Accepted table contract |
| `Id` | `int`, primary key, database identity | Accepted key behavior |
| `PublicId` | `uniqueidentifier NOT NULL`, unique index, application assigned, no database default | Accepted GUID generation/uniqueness |
| `Code` | Required `nvarchar(50)` with a value converter; unique index | SQL representation/conversion proposed; approved normalized code/uniqueness rules |
| `DisplayName` | Required `nvarchar(200)`, not unique | SQL representation proposed; approved required/trimmed/length behavior |
| `CreatedAtUtc` | Required `datetimeoffset(7)`, application assigned, no database default | SQL precision proposed; approved UTC DateTimeOffset creation behavior |

Propose `Latin1_General_100_BIN2` at column level for Code, comparing canonical lowercase ASCII values directly. This extends the normalized-key approach proposed/accepted for account keys; the organization column's collation requires its own approval. Do not change database-wide collation or introduce display-name uniqueness. SQL padding/case behavior still needs real provider tests.

Suggested index names are `UX_Organizations_PublicId` and `UX_Organizations_Code`; names are proposals, not approved external contracts. SQL lengths/nullability and indexes do not implement the full input contract or enforce historical immutability. Domain creation and later controlled persistence operations must continue to enforce valid values. No new direct-SQL email/code validation policy is introduced here.

## OrganizationCode conversion and tracking

Propose one module-owned conversion from `OrganizationCode` to `Code.Value`, and back through `OrganizationCode.Create(storedValue)`. Configure Code as a scalar converted property, not an owned entity, navigation, or separate table. Required nullability must be configured explicitly because EF converters do not handle database nulls themselves.

The immutable sealed record has value equality. Verify change tracking for equivalent instances; add a custom ValueComparer only if the selected provider/configuration requires it, rather than creating one without evidence. Queries should compare a validated OrganizationCode value with the mapped property; test translation instead of assuming member access such as `Code.Value` translates.

Conversion on read can normalize stored strings. Consequently, successful materialization alone does not prove the database contains canonical data. Integration tests should inspect raw stored Code, validate migration/import paths, and ensure application writes store lowercase normalized values. Corrupt rows that cannot create OrganizationCode should fail materialization rather than fabricate a valid code.

Reference: [EF Core value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions).

## Entity materialization

EF Core can bind mapped scalar properties to private constructors and can set properties with private setters. Constructor parameter names/types must match mapped CLR properties. [EF Core constructor binding](https://learn.microsoft.com/en-us/ef/core/modeling/constructors)

The existing Organization constructor accepts Code/DisplayName and generates a new GUID/time. Using it during database reads would generate transient creation values before persisted properties are populated. Do not assume this is a desirable rehydration path.

Propose a separately reviewed Domain change: a private materialization constructor accepting all mapped properties (`id`, `publicId`, `code`, `displayName`, `createdAtUtc`) with no GUID/time generation, while keeping the public Create factory as the only ordinary creation API. Refactor the creation constructor path as needed so constructor binding is unambiguous. Do not rely on EF-specific annotations in Domain or add a public parameterless constructor exposing invalid state. This adjustment is not implemented by this document and must have explicit implementation approval.

Tests must persist a known PublicId/time and load a fresh no-tracking instance, proving database identity, Code, name, GUID, and UTC timestamp survive. Metadata-only model tests cannot establish correct materialization. A UTC-offset check such as `DATEPART(TZOFFSET, CreatedAtUtc) = 0` is a possible later SQL guard, not an approved constraint in this proposal.

## Proposed project structure

| Project | Owned responsibilities | Proposed references |
| --- | --- | --- |
| Existing `GeoSurvey.Organizations.Domain` | Organization and OrganizationCode rules | No package/project dependencies |
| New `GeoSurvey.Organizations.Infrastructure` | Organization entity configuration, Code conversion, explicit mapping registration | Organizations Domain and necessary EF Core relational mapping APIs |
| Existing `GeoSurvey.Identity.Infrastructure` | IdentityAccount configuration and explicit Identity mapping registration | Existing Identity dependencies plus reviewed EF mapping dependencies |
| New `GeoSurvey.ControlPlane.Infrastructure` | Global context composition, SQL Server provider setup, design-time construction, global migrations | Organizations Infrastructure and Identity Infrastructure; only direct Domain references justified by composition code |
| API composition root | Register the approved control-plane services when runtime use cases exist | ControlPlane Infrastructure at the approved integration step |

These are proposed names and references, not instructions to generate empty projects immediately. Package versions and exact Identity context base type/dependent tables need their own implementation plan. Do not add a default global role system that replaces tenant custom RBAC.

## Context composition

Propose a single `GlobalControlPlaneDbContext` in ControlPlane Infrastructure for global identity/registry persistence. It calls the appropriate Identity base model configuration first, then explicit module mapping registration, and finally the composition-level scalar account-to-organization FK with NO ACTION. Identity's module mapping need not reference the Organizations Domain type merely to establish this cross-module database FK; composition can connect the two accepted types without adding a module-to-module dependency.

Do not scan every loaded assembly indiscriminately for configurations; explicit registration makes ownership and included tables reviewable. Verify exactly one Organizations table and one Accounts table, required auxiliary Identity records, no duplicate account/discriminator mappings, and no accidental tenant operational tables.

One global migrations assembly/history belongs to the ControlPlane project. Propose `controlplane.__EFMigrationsHistory` as the history table name; this remains subject to approval. Use one consistent configuration for design time and runtime. Credentials come from approved environment configuration/user-secrets; never commit a design-time connection string. Apply migrations through a reviewed deployment step, not each module/application replica at startup.

## Runtime module boundaries

Composition may reference module infrastructure; neither module may reference ControlPlane Infrastructure in return. That would create a cycle. No module can acquire another module's DbContext by using the shared database as justification.

Propose future use-case-specific persistence ports in the owning module's Application layer, implemented by control-plane composition adapters that access only that module's mapped records. Register those adapters at the host composition root. Application use cases receive their own capability, not the unrestricted global DbContext or a generic cross-module query interface. Identity-specific managers/stores require the same review so EF-backed Identity services do not expose organization-table access to unrelated use cases.

Do not create speculative ports, repositories, or adapters in this design-only task. The eventual port shape must follow the first approved use case. Runtime cross-module requests use public application contracts; transaction coordination spanning module ownership requires explicit design rather than one module manipulating another's records.

## SQL Server verification plan

- Verify model table/schema names, lengths, nullability, generated integer key, and application-assigned PublicId/time.
- Round-trip OrganizationCode as a converted scalar and verify record equality/change tracking and query translation.
- Save and reload a fresh Organization without generating replacement identity/time; verify UTC offset/precision behavior.
- Reject duplicate code/PublicId including concurrent inserts; allow duplicate display names.
- Check code comparison and raw persisted normalization; do not mistake binary collation for domain validation.
- Verify organization creation precedes the account FK in generated migration SQL and NO ACTION prevents cascading account deletion.
- Verify context contains only intended global entities and one migration history. Tenant databases retain separate mappings/histories.
- Enforce project reference direction and keep Domain dependency-free; future adapters must have tests for module-owned query scope.

Use isolated real SQL Server test databases for provider behavior; in-memory or SQLite tests are insufficient for collation/index/FK guarantees. Existing 87 passing object/architecture/integration tests do not validate any of these unimplemented mappings.

## Decisions required before implementation

1. Accept or revise organization SQL types/precision, conversion, Code collation, and the private materialization-constructor strategy.
2. Accept or revise proposed project/context names, explicit registrations, composition-level FK, and migration history location.
3. Approve a small first implementation slice, its packages, and test infrastructure. Do not implement all persistence, login, bootstrap, MFA, and provisioning at once.

Items 1 and 2 are accepted within the scope of records 0017 and 0018; extra SQL guards and unselected implementation details remain proposals. No code, package, DbContext, migration, or database is created by this document. Next, propose one small Organization mapping/materialization implementation slice with appropriate tests; do not create the entire control plane at once.
