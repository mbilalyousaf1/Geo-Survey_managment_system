# Authenticator secret storage and pending enrollment

Status: Accepted and implemented bounded persistence slice, 2026-11-17. The owner approved this proposal. See [decision 0033](../decisions/0033-pending-totp-enrollment-storage.md) and [implementation evidence](../development/pending-totp-enrollment.md). Completing Phase 1 remains the overall objective, subject to the existing review workflow.

## Purpose and scope

Persist an encrypted authenticator secret while an administrator completes enrollment. An enrollment challenge is permission to continue the restricted enrollment flow, not proof that MFA is configured. Persisting a pending secret must never set TwoFactorEnabled or issue an administrator token.

## Approved design

Add an Identity-owned authenticator credential model mapped in the composed global context. Use AccountId as its unique account association, store only Data Protection ciphertext, associate pending enrollment with the server-validated enrollment challenge, and persist lifecycle timestamps in UTC. Account and challenge foreign keys use NO ACTION. No raw secret, QR payload, or recovery code is stored in this table.

Pending state is usable only through its still-valid account/stamp-bound enrollment challenge. A replacement challenge must not adopt an old pending secret. A completed credential must not be overwritten through initial enrollment. The exact replacement/confirmation transaction must enforce these rules server-side, with row locking and constraints rather than only preliminary reads.

For the next bounded implementation, add pending-secret persistence and its trusted internal service only. Use the existing ITotpSecretProtector; receive secret material from the trusted future enrollment generator, not an HTTP client. Resolve the account/challenge server-side, validate eligibility, and store ciphertext atomically. Do not implement enrollment confirmation, TOTP generation/verification parameters, recovery, or login implicitly. Confirmation will be designed with TOTP verification and atomic challenge consumption before changing MFA state.

## Affected components

- Identity Infrastructure: model/mapping, pending-enrollment service and narrow persistence contract under Mfa.
- ControlPlane Infrastructure: EF implementation, context mapping, migration/snapshot, review SQL.
- Unit and SQL integration tests: ciphertext-only persistence, account/stamp/challenge binding, expiry/replacement, concurrent requests, refusal for completed MFA, and rollback.
- Development notes/decision record and relevant schema-count assertions.

No new dependency, API endpoint, client UI, or tenant database access is included in this slice. The concrete table is PendingTotpEnrollments, containing only pending ciphertext, AccountId, ChallengeId, and CreatedAtUtc. It is not a confirmed authenticator credential. TOTP parameters, attempt/replay controls, recovery-code policy, authentication sessions, residency/provisioning, and onboarding remain separate review boundaries.
