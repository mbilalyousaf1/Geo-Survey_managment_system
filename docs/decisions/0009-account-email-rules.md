# 0009: Account email normalization and uniqueness

Status: Accepted

Accepted and recorded: 2026-11-12

## Context

Organization and platform accounts are independent. The [identity-model proposal](../architecture/identity-data-model-proposal.md) needs consistent account lookup rules and scoped uniqueness. The product owner approved the policy below; the remaining model and physical schema remain proposals.

## Decision

| Aspect | Approved rule |
| --- | --- |
| Input | Trim surrounding whitespace; reject embedded whitespace |
| Validation | Require an email address; select the exact validator during implementation |
| Lookup | Case-insensitive comparison using consistent invariant normalization |
| Organization uniqueness | Unique within each organization; reusable across different organizations |
| Platform uniqueness | Unique within the platform-account scope |
| Across scopes | The same email may identify separate platform and organization accounts without linking them |
| Storage | Preserve the trimmed address for display and store a normalized lookup value |
| Provider-specific rewriting | Do not remove dots or `+tags` |

For example, ` Alice.Example+field@Example.com ` retains `Alice.Example+field@Example.com` for display. Casing differences must compare equal within the same account scope; dots and tags remain part of the address. This is GeoSurvey's account policy, not a claim that every email provider treats addresses identically.

## Consequences

- Account creation and login must apply the same normalization policy. Email alone still cannot select an organization account without organization context.
- Duplicate protection must use the normalized value and the correct scope. Application pre-checks cannot replace concurrent-safe database constraints.
- Do not automatically link accounts, share credentials, or grant permissions based on a matching email.
- Syntax validation does not prove address ownership or deliverability. Email verification and onboarding remain separate decisions.
- No email validator, value object, account entity, database constraint, or endpoint is implemented by this record.

## Remaining decisions

- The initial syntax, length, address-format, and normalizer choices were subsequently approved in [record 0013](0013-email-validation-contract.md). Actual implementation remains pending.
- Physical database representation, index/collation choices, and handling of retired/deleted accounts.
- Email changes, ownership verification, and recovery workflows.

Record 0013 subsequently selects uppercase invariant lookup via Identity, ASCII-only addresses, and no automatic internationalized-address conversion. The broader uniqueness and no-linking rules above remain unchanged.

## Related documentation

- [Master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md)
- [Separate organization accounts](0003-separate-organization-accounts.md)
- [Platform accounts](0006-platform-administrator-accounts.md)
- [Decision index](README.md)
