# 0010: ASP.NET Core Identity with EF Core

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

GeoSurvey requires independent organization accounts, separate platform accounts, scoped email uniqueness, mandatory platform TOTP, and controlled first-administrator bootstrap. The product owner approved ASP.NET Core Identity with EF Core as the implementation approach after reviewing these requirements. Exact physical account storage remains subject to design approval.

## Decision

- Use ASP.NET Core Identity with EF Core persistence for identity in the combined global control-plane database.
- Keep Identity and EF Core dependencies and persistence-specific models in Infrastructure. Domain remains dependency-free; application-facing abstractions will be added only for approved use cases that need them.
- Explicitly enforce organization-scoped account lookup, platform-account separation, and the approved email policy. Framework defaults are not evidence that these requirements are met.
- Use GeoSurvey application flows for organization login, first-administrator bootstrap, and mandatory platform MFA.
- Design JWT issuance, refresh-token rotation/reuse detection, authenticator-secret protection, recovery-code storage, and session behavior separately against the master specification.

## Consequences and limits

Identity provides reusable account-management components, model/store extension points, and TOTP support. It does not automatically establish GeoSurvey tenant isolation or authorize platform access after MFA. The EF Core model and user lookup/validation paths must be reviewed before adopting defaults.

The logical OrganizationAccount, PlatformAccount, MFA, and recovery records in the model proposal do not yet mandate separate physical tables. Schema shape, integer Identity keys with public GUIDs, indexes, account discriminators or types, normalized identifiers, stores, and validators remain implementation-design choices. Shared storage must not imply shared credentials or cross-scope account linking.

Do not expose generic Identity registration/login endpoints or default UI merely by selecting the framework. No public platform-admin registration is approved, and organization login requires organization context. GeoSurvey's tenant custom RBAC remains a separate authorization concern; framework roles must not silently replace it.

No package version, DbContext, Identity user class, endpoint, migration, or database is introduced by this documentation task. Package/version selection and specific customizations require their own approved implementation scope.

## Remaining design work

- Physical account representation and all lookup/validation paths needed for scoped uniqueness.
- Exact normalization/validator implementation consistent with approved email rules.
- MFA enrollment/access gating, secret protection, recovery, and concurrency controls.
- JWT/refresh-token lifecycle, bootstrap, and module registration/contracts.

## References

- [Microsoft: Identity model customization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0)
- [Microsoft: Identity managers and stores](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-custom-storage-providers?view=aspnetcore-10.0)
- [Microsoft: MFA](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-10.0)
- [Identity data model proposal](../architecture/identity-data-model-proposal.md)
- [Decision index](README.md)
