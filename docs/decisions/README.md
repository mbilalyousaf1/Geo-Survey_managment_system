# Architecture decision records

Current Phase 1 delivery: [0041: Local Docker submission](0041-phase-1-local-submission-delivery.md), accepted 2026-11-18. It supersedes historical pending-public-integration statements below within its approved scope. See the [actual validation evidence](../submission/phase-1/validation-results.md).

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
| [0021: Platform Administrator claim](0021-platform-administrator-claim.md) | Accepted | Product owner approved the server-managed administrator claim, atomic bootstrap account/claim creation, and scope/MFA authorization requirements on 2026-11-16 |
| [0022: Bootstrap transaction and application lock](0022-bootstrap-transaction-and-lock.md) | Accepted | Product owner approved a transaction-owned SQL Server bootstrap lock, atomic creation, recheck under lock, and 30-second lock timeout on 2026-11-16 |
| [0023: Bootstrap base password policy and hashing](0023-bootstrap-base-password-policy.md) | Accepted | Product owner approved NFC/Unicode length and input rules, no composition/expiry rules, and Identity V3 hashing at 220000 iterations; blocklist source remains open |

Record numbers here identify documentation files. They are independent of the master specification's ADR identifiers: record 0002 does not replace master ADR-002, which covers request handling.

Additional accepted record: [0024: Local maintained password corpus](0024-local-password-corpus.md). The owner selected local validation on 2026-11-16; concrete corpus/operations remain proposed.

Source/operations accepted and implemented: [0025: Local password blocklist details](0025-local-password-blocklist-details.md), 2026-11-16. This resolves the additional details left open by 0024.

Operator tool accepted: [0026: Bootstrap console and operator access](0026-bootstrap-console-and-operator-access.md), 2026-11-16. A separate console and dedicated operator credential are approved; concrete permission/implementation details and deletion/recovery behavior remain open.

Permanent completion accepted: [0027: Permanent bootstrap completion](0027-permanent-bootstrap-completion.md), 2026-11-16. A durable marker disables bootstrap after initial success even if the administrator is deleted/locked out; schema/code and recovery remain separate work.

When a decision changes, add a new reviewed record and mark the earlier record as superseded with a link. Preserve the earlier reasoning. Use a Proposed status for choices awaiting approval; use Accepted only for choices already approved. An acceptance date records approval, while a recorded date indicates when the documentation was written.

Service/permissions accepted: [0029: Bootstrap service and permissions](0029-bootstrap-service-and-permissions.md), 2026-11-17. The service and restricted-role test slice are implemented; console and real operator credential provisioning remain pending.

Storage schema accepted and implemented: [0028: Bootstrap-completion schema](0028-bootstrap-completion-schema.md), 2026-11-17; fixed singleton key, UTC timestamp, no account FK/seed/reset method, and a separately tested second migration.

Restricted MFA challenges: [0030](0030-restricted-mfa-challenges.md), accepted 2026-11-17. Ten-minute opaque tokens, hash-only storage, purpose/account/stamp binding, replacement, and one-time consumption.

Account-bound TOTP secret protection: [0031](0031-totp-secret-protection.md), accepted 2026-11-17. Data Protection uses versioned account purposes; durable certificate-protected host key configuration remains pending.

API key host: [0032](0032-api-data-protection-host.md), accepted 2026-11-17. Mounted PFX, explicit external persistent ring, environment-specific application name, startup verification, and safe failures.

Pending authenticator storage: [0033](0033-pending-totp-enrollment-storage.md), accepted 2026-11-17. Ciphertext-only account/challenge-bound pending state, serialized writes, replacement isolation, and no implicit MFA enablement.

TOTP primitive: [0034](0034-totp-primitive.md), accepted 2026-11-17. Random 160-bit Base32 secret, six-digit HMAC-SHA1, 30-second steps, +/- one-step verification, and matched-step reporting. Persistent replay/attempt controls remain pending.

MFA attempt/replay controls: [0035](0035-mfa-attempt-replay-controls.md), accepted 2026-11-17. Five failures within 15 minutes block MFA for 15 minutes across replacement challenges; persistent monotonic step acceptance requires a completion transaction.

MFA enrollment confirmation: [0036](0036-mfa-enrollment-confirmation.md), accepted 2026-11-17. Internal atomic TOTP confirmation, stamp rotation, confirmed ciphertext, and ten one-time-delivery 128-bit recovery codes persisted as account-bound hashes. Recovery consumption and public login remain pending.

Platform password entry: [0037](0037-platform-password-entry.md), accepted 2026-11-18. Scope-specific real password verification, five-consecutive-failure/15-minute lockout, rehash/stamp revalidation, and restricted MFA challenge issuance. No authenticated session or public endpoint is enabled.

Confirmed-factor sign-in verification: [0038](0038-platform-mfa-verification.md), accepted 2026-11-18. Token-resolved public subject, actual confirmed TOTP, current eligibility/lockout checks, and atomic replay/nonce updates within a caller-owned read-committed completion transaction. Session/token issuance remains separate.

Session/refresh storage: [0039](0039-session-refresh-token-storage.md), accepted 2026-11-18. Global account-bound token families, digest-only refresh records, UTC/integrity constraints, one unconsumed token per family, and retained consumption history. Session issuance and concrete lifecycle policies remain separate.

Platform session start/lifetimes: [0040](0040-platform-session-start-and-lifetimes.md), accepted 2026-11-18. Transaction-owned actual MFA plus initial session/digest persistence; five-minute future access maximum, thirty-minute refresh inactivity, eight-hour absolute limit, and independent families. JWT/public authentication and refresh/revocation flows remain separate.
