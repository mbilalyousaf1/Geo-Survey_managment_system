# 0013: Email validation and normalization contract

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

[Record 0009](0009-account-email-rules.md) establishes scoped, case-insensitive email lookup and uniqueness. The product owner approved the exact initial validation/normalization contract below. This decision does not implement it.

## Decision

Apply the same contract to organization and platform account creation and login:

1. Reject null input. Trim surrounding whitespace using `.Trim()`.
2. Reject empty input and any remaining whitespace.
3. Accept ASCII addresses only, up to 254 characters after trimming. Reject non-ASCII address characters rather than silently transliterating or converting them.
4. Require basic syntax validation with .NET `EmailAddressAttribute`.
5. Preserve the trimmed, original-cased address as the display email.
6. Produce the lookup value using Identity's `UpperInvariantLookupNormalizer` on the validated trimmed address.
7. Preserve dots and `+tags`; enforce uniqueness in the approved organization or platform scope.

Example display value: `Alice.Example+field@Example.com`; lookup value: `ALICE.EXAMPLE+FIELD@EXAMPLE.COM`.

## Consequences

Validation combines explicit guards and the framework validator. The attribute alone is not the complete contract and does not prove address ownership or deliverability. This is basic application syntax validation, not a full RFC-conformance or mailbox-verification guarantee.

Use one implementation path for writes and lookups so casing/whitespace handling cannot drift. Identity-specific normalization remains in Infrastructure; Domain must not acquire Identity dependencies. Exact API errors, DTOs, and component placement require implementation approval.

ASCII validation occurs after trimming; non-ASCII characters inside the trimmed address are rejected. Internationalized email addresses and automatic internationalized-domain conversion are not added by this policy. Any expansion of supported formats requires a new reviewed decision.

Database columns must accommodate approved display and lookup lengths, with required values and scoped unique indexes. Exact SQL types/collation and EF mappings remain to be designed. The normalizer is not a uniqueness check or authorization mechanism.

This contract was initially recorded without implementation. The subsequently approved `AccountEmail` component now implements the guards, syntax check, and normalization in Identity Infrastructure, using `Microsoft.Extensions.Identity.Core` `10.0.9`. Its unit tests cover 23 cases. No migration, account/login flow, or database constraint has been added. Email-change, ownership-verification, and recovery workflows remain separate.

## Verification required with implementation

Test null/empty input, trimming, embedded whitespace, non-ASCII rejection, 254/255-character boundaries, framework syntax failures, culture-independent normalization, dot/tag preservation, and consistency across account scopes. SQL Server integration tests must later verify the database's scoped comparison/uniqueness behavior.

## References

- [.NET EmailAddressAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.emailaddressattribute?view=net-10.0)
- [Identity UpperInvariantLookupNormalizer](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.upperinvariantlookupnormalizer?view=aspnetcore-10.0)
- [Decision index](README.md)
