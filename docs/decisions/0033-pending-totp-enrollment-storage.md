# 0033: Pending TOTP enrollment storage

Status: Accepted, 2026-11-17.

The owner approved the [pending-secret persistence proposal](../architecture/authenticator-secret-storage-proposal.md) while retaining Phase 1 as the overall objective and the significant-change approval workflow.

Identity Infrastructure owns PendingTotpEnrollment, its mapping, a narrow persistence contract, and PendingTotpEnrollmentService. ControlPlane Infrastructure owns the EF implementation because it owns the composed global context. Reuse ITotpSecretProtector and the existing enrollment-challenge eligibility query. No dependency, public endpoint, tenant database, secret generator, authenticator verification, or token issuer is added.

Use identity.PendingTotpEnrollments with AccountId as the primary key, unique ChallengeId, protected ciphertext, and CreatedAtUtc. Two NO ACTION foreign keys bind the row to its account and the same account's challenge. The latter uses a composite reference to MfaChallenges(Id, AccountId), backed by a new alternate key. The fourth migration adds these constraints/table without changing prior data or seeding anything. Pending rows do not enable TwoFactorEnabled or consume a challenge.

The trusted future enrollment flow supplies a generated secret and account/challenge; never pass arbitrary client-supplied secret material to this internal service. The service protects plaintext before persistence and hashes the opaque token. The store serializes writes with the account lock used by challenge replacement, then locks the challenge before checking validity. Time is read after acquiring locks. A repeated save for the same challenge is refused, preserving the first ciphertext. A valid replacement challenge may replace the pending row with freshly protected material; it cannot retrieve/adopt the prior challenge's material. This replacement concerns unconfirmed MFA state, not historical survey/audit data or credential reset.

Reads require the current enrollment purpose/account/stamp, administrator claim, no current lockout, MFA disabled, and an unconsumed/unrevoked/unexpired challenge, in one SQL query. Unreadable ciphertext fails closed through the existing protector. The existing sensitive-data-logging guard applies. No cleanup/retention schedule or confirmed-credential replacement workflow is invented.

The store can participate in a caller-owned transaction and otherwise commits its own. It uses parameterized insert/update commands without flushing unrelated tracked entities. The caller owns rollback/commit on an ambient transaction. A future confirmation workflow must revalidate and atomically verify/consume/store confirmed MFA state; this decision does not authorize enabling MFA without that proof.

See [implementation and verification](../development/pending-totp-enrollment.md).
