# Physical identity account storage proposal

Status: Single-table strategy, scope constraints, and scoped email uniqueness accepted on 2026-11-12 in [record 0011](../decisions/0011-scoped-single-account-table.md). Detailed mappings and implementation remain proposed.

Prepared: 2026-11-12

## Purpose and baseline

Translate the [logical identity model](identity-data-model-proposal.md) into a candidate ASP.NET Core Identity/EF Core mapping. [Record 0010](../decisions/0010-aspnet-core-identity-ef-core.md) approves the framework, while records 0003-0009 define independent accounts, login scope, bootstrap, MFA, and email rules. Those business rules remain authoritative; the physical design below is a proposal.

## Recommended account representation

Use one Identity account table in the global control-plane database, with a distinct row for every account. Sharing a table does not share passwords, account identity, sessions, or permissions. The same person/email in two organizations and platform scope would have three unrelated account rows.

An Infrastructure identity model based on `IdentityUser<int>` is proposed. Do not make Domain entities inherit Identity types. A candidate physical table name is `identity.Accounts`; final SQL schema/naming still require review; account key generation is approved in record 0012.

| Field | Proposed meaning/constraint |
| --- | --- |
| `Id` | Database-generated internal integer primary key (approved in record 0012) |
| `PublicId` | Required unique account GUID generated with `Guid.NewGuid()` in the application; fixed after creation (record 0012) |
| `AccountScope` | Required explicit Platform or Organization value; persisted representation to be selected |
| `OrganizationId` | Nullable integer foreign key to the global organization record, with scope checks below |
| `Email` | Required trimmed display address, preserving approved dots/tags and casing |
| `NormalizedEmail` | Required lookup value from Identity UpperInvariantLookupNormalizer; approved trimmed ASCII email limit is 254 characters (record 0013); exact SQL mapping remains open |
| `UserName`, `NormalizedUserName` | Required opaque internal identifier, not the user's email or public login input |
| Identity credential/security fields | Independent password hash, security/concurrency stamps, and other fields actually needed by the reviewed Identity configuration |
| `CreatedAtUtc` | UTC creation timestamp |

Do not infer final lockout, verification, status, or deletion policy from Identity's available columns. Platform scope identifies an authentication boundary, not an automatic grant of administrative permissions.

## Proposed relational constraints

| Constraint/index | Purpose |
| --- | --- |
| Scope check | Permit only Platform with `OrganizationId IS NULL`, or Organization with `OrganizationId IS NOT NULL`; reject every other value/combination |
| Required organization foreign key for organization scope | Reject references to organizations that do not exist |
| Unique organization email index | Unique `(OrganizationId, NormalizedEmail)` filtered to organization scope |
| Unique platform email index | Unique `NormalizedEmail` filtered to platform scope |
| Unique `PublicId` | Public identity is not an internal sequential key |
| Unique `NormalizedUserName` | Preserve global uniqueness for opaque Identity usernames without blocking scoped email reuse |

Make required values non-null explicitly. Scope check expressions must reject invalid scope values and must not rely on SQL's unknown/null evaluation to reject a row. Define normalized-column collation and indexes to match the selected normalizer, rather than inheriting an accidental database default.

Application duplicate checks provide friendly validation, while database indexes enforce uniqueness under concurrency. No soft-delete filters or email reuse after deletion are approved by this proposal. Foreign-key cascade behavior and account lifecycle remain open.

## Identity usernames and customization

Microsoft's default model has a unique normalized username index and a non-unique normalized email index. Email-as-username would therefore block independent same-email accounts unless deliberately customized. See [Identity model customization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0).

Approved in [record 0012](../decisions/0012-account-identifiers-and-usernames.md): generate account PublicId with `Guid.NewGuid()` in the application and derive UserName using `PublicId.ToString("N")`. Keep both fixed after creation. Users do not enter the internal username. NormalizedUserName must be produced consistently through the chosen Identity normalizer; exact implementation remains to be designed.

Do not enable a global unique-email check and assume it enforces organization uniqueness. Registration and credential-update validation must use the intended scope and organization, exclude the account itself on updates, and rely on the corresponding database index for races. Normalization must be shared by writes and lookups and follow record 0009; do not strip dots or tags.

Review all manager/store paths before implementation. Do not invoke an email-only global lookup against a table where duplicates are legitimate. Default Identity UI/endpoints and username-based sign-in must not become alternative routes around GeoSurvey's scoped login. Whether scoped behavior is supplied through application services, custom validators, store changes, or a combination is a separate detailed implementation proposal, not an instruction to replace every Identity component.

## Organization login lookup

1. Normalize and validate organization code using the approved rules and `OrganizationCode` behavior.
2. Resolve the intended organization through the trusted global registry. Do not interpret the code as a database name or connection string.
3. Normalize email using the approved account policy.
4. Query accounts with Organization scope, the resolved `OrganizationId`, and matching `NormalizedEmail`.
5. Verify credentials and any required account policies for that exact row. Do not fall back to another organization or platform scope.
6. Bind subsequent session/token identity to that specific account and organization; authorize operational access before tenant database routing.

Failure messages, enumeration resistance, rate limits, account lifecycle checks, and exact token/session behavior still need their own design. This lookup proposal does not bypass those requirements.

## Platform login lookup

1. Normalize email with the same approved email policy.
2. Query Platform scope with `OrganizationId IS NULL` and matching `NormalizedEmail`.
3. Verify credentials for that row. Never fall back to an organization account.
4. Enforce platform MFA: incomplete enrollment or password verification alone cannot authorize platform administration.
5. Establish platform-scoped identity and evaluate platform permissions. No implicit organization session or tenant-data access is granted.

## Auxiliary Identity records and module boundary

Identity-managed dependent records must reference the exact account ID, not email or organization code. Review any roles/claims/token relationships for scope isolation before enabling them. Tenant roles and project/area permissions remain in their tenant database; a global Identity role table must not replace the selected custom tenant RBAC by default.

The logical MFA enrollment and recovery-code records are not finalized physical tables. Authenticator-secret protection, hashed recovery codes, atomic single use, and prevention of MFA/session scope mixing require separate storage/flow review. Identity's presence alone is not proof those protections are implemented. Refresh-token families and rotation are also separate design work.

Organizations owns organization identity and code rules. Identity consumes explicit application contracts for organization resolution; it must not directly manipulate another module's DbContext or private tables. Proposed relational foreign keys in the shared control-plane database do not authorize cross-module data access. DbContext ownership and migration coordination remain to be designed.

## Alternative: separate physical account tables

Separate platform and organization Identity user tables could express account scope structurally. They would require review of user types, stores/manager registrations, dependent Identity tables, and migration ownership for each mapping. Separate tables still need organization-scoped email handling and explicit authorization.

The accepted single-table approach avoids duplicating the account infrastructure while making scope explicit through constraints and query predicates. Its tradeoff is that every account lifecycle and lookup path must preserve scope; forgetting a predicate could cross a security boundary. The mapping is not implemented. Record 0011 approves the table/scope/uniqueness strategy, not every candidate field or implementation detail in this document.

## Required implementation tests

- Same normalized email succeeds in two organizations and in platform scope as separate accounts; a duplicate within either scope fails, including concurrent inserts.
- Invalid scope/null combinations and nonexistent organization references fail database constraints.
- Platform login cannot authenticate organization credentials, and organization login cannot search another organization or platform account.
- Internal usernames remain unique and independent of email; supported manager/store flows do not perform ambiguous global email lookups.
- Account/public IDs cannot bypass scope authorization in password changes, recovery, MFA, or session issuance.
- SQL Server integration tests verify actual filtered indexes, collation, and check-constraint behavior; in-memory tests alone are insufficient.

No tests are executed for this documentation-only update. The table strategy is accepted; implementing it still requires approval of a concrete task and resolution of affected open choices.

## Review decisions still needed

- Single-table storage, explicit scope constraints, and scoped email uniqueness are accepted in record 0011; exact schema names, columns, constraints, and indexes still need design.
- Account generation and username format are approved in record 0012; exact mappings and error handling remain to be designed.
- Implement the email contract approved in [record 0013](../decisions/0013-email-validation-contract.md); SQL comparison/collation and mapping still require review.
- Define module contracts, DbContext/migration ownership, identity customizations, MFA protection, and session flows in subsequent small tasks.

Do not add packages, generate migrations, or create account tables solely because this proposal exists.
