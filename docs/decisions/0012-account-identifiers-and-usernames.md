# 0012: Account identifiers and internal usernames

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

The [single-account-table decision](0011-scoped-single-account-table.md) requires independent accounts and scoped email reuse. Identity's unique internal username must therefore be independent of email. The product owner approved the generation rules below.

## Decision

- Use a database-generated integer `Id` for the internal relational key.
- Generate account `PublicId` in the application using `Guid.NewGuid()`.
- Derive Identity `UserName` from the account PublicId using the 32-character hexadecimal format without hyphens (`PublicId.ToString("N")`).
- Keep the account PublicId and internal username fixed after creation, including when display email changes.
- Users enter email at login, together with organization code for organization accounts. They do not enter the internal username.

For example, PublicId `3f604aa6-9143-448f-9572-3fca0a66b13d` gives username `3f604aa69143448f95723fca0a66b13d`. Identity's normalized username must be produced consistently through the chosen normalizer; it is not a separate user input.

## Consequences

The application can assign public identity and username before persistence assigns the integer key. Required unique indexes still enforce PublicId and normalized-username uniqueness; GUID generation does not replace database constraints.

PublicId and username are identifiers, not credentials or authorization. Internal integer IDs must not be exposed in public routes. A public GUID does not grant cross-account or cross-tenant access.

These rules settle account identifier generation and username format, not the full Identity schema, normalizer implementation, account lifecycle, collision/error handling, or session design. No account factory, Identity user class, mapping, or migration is implemented by this documentation update. This account decision does not silently finalize generation for every future GIS, survey, or other entity.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Physical storage proposal](../architecture/identity-account-storage-proposal.md)
- [Decision index](README.md)
