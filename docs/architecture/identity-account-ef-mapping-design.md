# Identity account EF Core mapping design

Status: Account mapping choices accepted in [record 0014](../decisions/0014-account-sql-mapping-choices.md); module ownership, one composed global context/migration stream, and organization table/key contract accepted in [record 0015](../decisions/0015-control-plane-persistence-ownership.md). Detailed implementation remains proposed/open. No implementation is authorized by this document alone.

Prepared: 2026-11-13

Implementation update, 2026-11-14: the separately approved Identity mapping slice is implemented with 35 passing real SQL Server tests. See [implementation, verification, and limitations](../development/identity-persistence-tests.md). Account/supporting mappings and shared Version 2/key-length settings now exist in Identity Infrastructure. The context is test-only; production global composition, Organization FK, migrations, and API/credential flows remain unimplemented. Statements below saying no Identity mapping exists describe the original proposal stage, not current implementation status.

Current decision update: [record 0019](../decisions/0019-role-free-identity-context.md) accepts `IdentityUserContext<IdentityAccount, int>` as the future global context base. Preserve base configuration, then explicitly apply module mappings. Supporting tables, their exact configuration, and implementation still require review. Organization persistence is now implemented and verified separately in [Organization mapping tests](../development/organization-persistence-tests.md); earlier statements below about it being pending describe the original proposal stage. Identity mappings, the global context, and migrations remain unimplemented.

## Scope and existing implementation

Supporting-table update: [record 0020](../decisions/0020-identity-supporting-table-mappings.md) accepts `identity.AccountClaims`, `identity.AccountLogins`, and `identity.AccountTokens`, framework keys and required account relationships, explicit 128-character login/token key fields, NO ACTION deletion, and Identity schema Version 2 without role/passkey tables. Runtime/test/design-time settings must agree. This resolves the corresponding earlier open choices; no Identity persistence implementation exists yet.

The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md) and [account-storage proposal](identity-account-storage-proposal.md) establish the context. Records [0011](../decisions/0011-scoped-single-account-table.md), [0012](../decisions/0012-account-identifiers-and-usernames.md), and [0013](../decisions/0013-email-validation-contract.md) approve the single-table/scoped-account strategy, identifiers, and email contract respectively.

`IdentityAccount : IdentityUser<int>` and `AccountEmail` exist in Identity Infrastructure. Their factories initialize valid objects; inherited Identity fields remain mutable. No DbContext, EF Core provider, migration, account table, or organization persistence currently exists. This design proposes the next persistence rules without adding any of those components.

## Candidate field mapping

| Property | Proposed SQL/EF mapping | Basis |
| --- | --- | --- |
| Table | `identity.Accounts` | Proposed module SQL schema/table name |
| `Id` | `int`, primary key, database identity generation | Approved internal key strategy |
| `PublicId` | `uniqueidentifier NOT NULL`, unique index; application assigns value, no database GUID default | Approved `Guid.NewGuid()` strategy |
| `AccountScope` | `int NOT NULL` with explicit enum conversion | Proposed persisted encoding: Platform = 1, Organization = 2 |
| `OrganizationId` | Nullable `int`; FK to the global organization's internal key | Approved association; exact organization table mapping pending |
| `Email` | `nvarchar(254) NOT NULL` | Approved trimmed display address, ASCII validation in application |
| `NormalizedEmail` | `nvarchar(254) NOT NULL`, explicit binary collation below | Approved uppercase invariant normalization; physical representation proposed |
| `UserName` | `nvarchar(32) NOT NULL` | Approved GUID `N` format |
| `NormalizedUserName` | `nvarchar(32) NOT NULL`, explicit binary collation | Consistent Identity-normalized internal username |
| Other inherited fields | Review Identity's base mapping; preserve its concurrency/security semantics | No new credential/lockout business policies selected |

No creation timestamp is added to `IdentityAccount` by this design: its exact requirement/type and initialization need a later approved change. The earlier logical model's timestamp is still a candidate field. Required column mappings must be explicit despite nullable inherited CLR properties. Exact schema names, persisted enum values, and lengths must be reflected consistently at migration design time and runtime.

## Scope integrity

Proposed check name: `CK_Accounts_ScopeOrganization`. With `AccountScope NOT NULL`, the expression is:

```sql
([AccountScope] = 1 AND [OrganizationId] IS NULL)
OR
([AccountScope] = 2 AND [OrganizationId] IS NOT NULL AND [OrganizationId] > 0)
```

This rejects unknown scope values, Organization scope without a positive organization key, and Platform scope with an organization key. The FK separately verifies that a referenced organization exists. A nullability declaration is essential: SQL Server CHECK constraints alone do not reliably reject NULL through expressions that evaluate to UNKNOWN. [SQL Server constraint behavior](https://learn.microsoft.com/en-us/sql/relational-databases/tables/unique-constraints-and-check-constraints?view=sql-server-ver17)

The constraint validates row shape, not who is allowed to create it. Platform scope is not proof of administrative permission; creation services and authorization still need review. Checks/indexes also cannot enforce historical immutability of PublicId, scope, organization association, or username. Controlled update paths must preserve those invariants; review any stronger database protection separately.

## Indexes and uniqueness

| Proposed index name | Key columns | Unique | Filter |
| --- | --- | --- | --- |
| `UX_Accounts_PublicId` | `PublicId` | Yes | None |
| `UX_Accounts_NormalizedUserName` | `NormalizedUserName` | Yes | None |
| `UX_Accounts_OrganizationEmail` | `OrganizationId`, `NormalizedEmail` | Yes | `[AccountScope] = 2` |
| `UX_Accounts_PlatformEmail` | `NormalizedEmail` | Yes | `[AccountScope] = 1` |

Configure these with explicit names, uniqueness, and SQL Server filters. Review Identity's base username index and email index configuration after `base.OnModelCreating`: customize the existing username index rather than accidentally adding a duplicate. There must be no unfiltered unique email index, which would block approved cross-organization email reuse. A non-unique global email index is optional and needs a measured query reason rather than automatic retention. EF Core supports composite, unique, and filtered indexes. [EF Core indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)

No deletion/status filters are proposed: introducing one would implicitly decide retired-account email reuse, which is still open. Duplicate creation must fail under the database's scoped indexes even if two requests both pass an earlier availability check. Translate a known constraint violation into the appropriate application error later; do not return raw database exceptions or email values to clients.

## Normalized-value comparison

Propose `Latin1_General_100_BIN2` at column level for `NormalizedEmail` and `NormalizedUserName`, rather than changing the entire database collation. The application performs the approved invariant normalization first; queries compare the stored normalized key directly. Binary comparison is proposed to avoid a database linguistic collation treating additional characters or punctuation as equivalent. It is case-sensitive by itself: it implements the approved case-insensitive account policy only when every write and lookup uses the same canonical normalization.

Do not use SQL `UPPER()`, query-time `ToUpper()`, or per-query collation overrides as a substitute for the approved normalizer. Such query transformations can also affect index use. [EF Core collation guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity)

Important limit: SQL Server string equality can treat trailing padding differently from .NET ordinal equality even with a binary collation. Therefore, binary collation alone is not an enforcement mechanism for the full email contract. The authoritative application path must reject untrimmed/invalid data and generate the canonical lookup value. SQL Server integration tests must verify punctuation, padding, and normalization behavior. Additional canonical-value/format checks for direct SQL writers need a separate concrete design; no full database email validator is promised here.

## Module ownership and organization FK

Ownership accepted in record 0015:

- Organizations owns its organization table, identity, and code. Identity owns account mappings and Identity-dependent records.
- Identity stores the scalar `OrganizationId`; no navigation exposing another module's Domain entity or DbContext is needed.
- Runtime organization resolution occurs through an explicit application contract. Identity must not use that contract as permission to query another module's private tables directly.
- A reviewed database migration can add the FK across module schemas in the same global database without giving Identity runtime ownership of Organizations data.
- Propose `NO ACTION` delete behavior on this FK to prevent account deletion cascading from organization deletion. This is a referential-integrity safeguard, not approval of an organization deletion/retention workflow.

The organization table/key contract is accepted: `organization.Organizations`, database-generated integer Id and unique public GUID PublicId. Minimal fields and application GUID generation are accepted in [record 0016](../decisions/0016-minimal-organization-entity.md), but entity/persistence implementation remains pending. Do not invent a stub entity or create an incomplete FK merely to make account mapping compile.

A control-plane Infrastructure project will compose module-owned mappings into one global DbContext and migration history. Organization table creation precedes the account FK. Exact project/context structure, mapping registration, restricted runtime persistence access, design-time factory, and packages remain unapproved. Do not let module startup independently apply competing migrations. Domain projects remain free of EF Core/Identity dependencies.

## Model configuration sequence for a future implementation

1. Select the approved global context composition and Identity context shape; include only reviewed supporting tables.
2. Call Identity's base model configuration, then apply the account table/properties, indexes, check, and eventual FK deliberately.
3. Ensure the private constructor/private-set fields materialize correctly, and prevent unintended shadow properties or tables.
4. Generate and review migration SQL only after organization FK ownership and all proposed mapping choices are accepted.
5. Run SQL Server integration tests before declaring persistence valid.

This sequence does not approve an `IdentityDbContext` variant, global role-table design, or generic authentication endpoints. Tenant roles stay tenant-owned. MFA secrets, recovery codes, refresh-token families, and authorization claims are separate mappings/flows and are not completed by an account table.

## SQL Server integration-test scenarios

| Scenario | Expected result |
| --- | --- |
| Platform without OrganizationId; Organization with existing positive OrganizationId | Rows can be persisted with valid required fields |
| Scope 0/unknown; Platform with organization; Organization with null/non-positive organization | Rejected by check/nullability constraints |
| Organization key does not exist | Rejected by FK |
| Same normalized email in two organizations and Platform scope | Three independent rows allowed |
| Duplicate normalized email in one organization or twice in Platform scope | Rejected, including concurrent inserts |
| Duplicate PublicId or normalized username | Rejected |
| Null email/normalized email/username/normalized username | Rejected |
| Valid 254-character email; 255-character email; 32/33-character usernames | Required configured lengths verified under the provider |
| Casing variants normalized through AccountEmail | Same scoped lookup key; duplicate rejected |
| Dots and `+tags` differ | Stay distinct as approved; no provider-specific rewriting |
| Rows inserted with bad padding/non-canonical casing outside application paths | Document actual SQL behavior; do not claim application normalization is database-enforced |
| Organization delete with dependent account | Blocked by proposed NO ACTION FK; no implicit account deletion |
| Private-set properties materialized after save/query | Scope, PublicId, organization ID, and normalized values match |
| Optimistic concurrency on supported Identity updates | Correct conflict handling; no last-write-wins assumption |

Use isolated test databases and real SQL Server provider behavior. EF InMemory/SQLite cannot prove SQL Server filtered indexes, collation, identity generation, or CHECK/FK semantics. No test has been run for these proposed mappings; the existing 76 passing tests do not validate database persistence.

## Approval points and next task

Before persistence implementation, review table/schema naming, integer scope encoding, column mappings, filtered indexes, normalized-column binary collation, and the proposed NO ACTION FK. Resolve the organization table/key contract and global DbContext/migration ownership separately.

The choices listed in records 0014, 0015, and 0016 are accepted. Recommended next small task: propose implementation of the minimal Organization entity and its unit tests before persistence mapping. Unaccepted examples and other suggestions in this document remain proposals. No package, DbContext, SQL script, migration, or database has been created by this documentation task.
