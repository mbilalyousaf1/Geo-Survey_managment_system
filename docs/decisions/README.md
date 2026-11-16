# Architecture decision records

These records capture the context, decision, and consequences of GeoSurvey's architecture choices. The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md) remains the source of truth. Recording an existing decision does not approve open business requirements or imply that all related code exists.

| Record | Status | Basis |
| --- | --- | --- |
| [0001: Modular monolith with Clean Architecture](0001-modular-monolith.md) | Accepted | Master specification ADR-001 and sections 3–4 |
| [0002: Separate mobile solution](0002-separate-mobile-solution.md) | Accepted | Approved Phase 0 mobile skeleton implementation |
| [0003: Separate accounts per organization](0003-separate-organization-accounts.md) | Accepted | Product owner resolved OPEN-001 on 2026-11-12 |
| [0004: Organization code at login](0004-organization-code-login.md) | Accepted | Product owner approved organization code + email + password for web/mobile on 2026-11-12 |
| [0005: Organization-code rules](0005-organization-code-rules.md) | Accepted | Product owner approved validation, normalization, uniqueness, selection, and immutability on 2026-11-12 |
| [0006: Platform Administrator account scope and login](0006-platform-administrator-accounts.md) | Accepted | Product owner approved separate platform scope and dedicated email/password login on 2026-11-12 |
| [0007: First Platform Administrator bootstrap](0007-first-platform-administrator-bootstrap.md) | Accepted | Product owner approved one-time operator-run bootstrap on 2026-11-12 |
| [0008: Mandatory Platform Administrator MFA](0008-platform-administrator-mfa.md) | Accepted | Product owner approved authenticator-app TOTP, enrollment before access, and single-use recovery codes on 2026-11-12 |
| [0009: Account email normalization and uniqueness](0009-account-email-rules.md) | Accepted | Product owner approved case-insensitive scoped lookup and uniqueness rules on 2026-11-12 |
| [0010: ASP.NET Core Identity with EF Core](0010-aspnet-core-identity-ef-core.md) | Accepted | Product owner approved the framework approach on 2026-11-12; schema and customizations still require review |
| [0011: Single Identity account table with explicit scope](0011-scoped-single-account-table.md) | Accepted | Product owner approved shared physical storage with independent accounts, scope constraints, and scoped email uniqueness on 2026-11-12 |
| [0012: Account identifiers and internal usernames](0012-account-identifiers-and-usernames.md) | Accepted | Product owner approved database integer IDs, application GUIDs, and GUID-derived fixed usernames on 2026-11-12 |
| [0013: Email validation and normalization contract](0013-email-validation-contract.md) | Accepted | Product owner approved ASCII-only email, 254-character limit, .NET basic validation, and Identity invariant normalization on 2026-11-12 |
| [0014: Account SQL mapping choices](0014-account-sql-mapping-choices.md) | Accepted | Product owner approved table/scope encoding, column mappings, binary normalized comparisons, filtered email uniqueness, and NO ACTION FK on 2026-11-13 |
| [0015: Control-plane persistence ownership and organization key contract](0015-control-plane-persistence-ownership.md) | Accepted | Product owner approved module mappings composed into one global context/migration stream and `organization.Organizations` key contract on 2026-11-13 |
| [0016: Minimal Organization entity](0016-minimal-organization-entity.md) | Accepted | Product owner approved minimal fields, GUID generation, display-name rules, and UTC creation timestamp on 2026-11-13 |
| [0017: Organization mapping and materialization](0017-organization-mapping-and-materialization.md) | Accepted | Product owner approved SQL mapping, Code conversion/collation, and private materialization-constructor strategy on 2026-11-13 |
| [0018: Global persistence composition structure](0018-global-persistence-composition.md) | Accepted | Product owner approved project/context names, dependency direction, explicit mapping/FK composition, and migration-history location on 2026-11-13 |
| [0019: Role-free Identity context base](0019-role-free-identity-context.md) | Accepted | Product owner approved `IdentityUserContext<IdentityAccount, int>`; concrete supporting tables and implementation remain subject to review |
| [0020: Identity supporting-table mappings](0020-identity-supporting-table-mappings.md) | Accepted | Product owner approved claims/login/token table names, Version 2 schema, 128-character key fields, required account FKs, and NO ACTION deletion on 2026-11-13 |

Record numbers here identify documentation files. They are independent of the master specification's ADR identifiers: record 0002 does not replace master ADR-002, which covers request handling.

When a decision changes, add a new reviewed record and mark the earlier record as superseded with a link. Preserve the earlier reasoning. Use a Proposed status for choices awaiting approval; use Accepted only for choices already approved. An acceptance date records approval, while a recorded date indicates when the documentation was written.
