# 0028: Bootstrap-completion schema

Status: Accepted

Accepted and recorded: 2026-11-17

The owner approved ControlPlane-owned table `controlplane.BootstrapCompletion`, required fixed integer PK Id 1 with CHECK `Id = 1`, and required application-assigned UTC `CompletedAtUtc datetimeoffset(7)`. No account FK, seed, or custom reset/delete method is added. This preserves completion independently of account deletion.

The approved storage slice includes the model/configuration, explicit global context composition, a new migration preserving the initial migration, marker-only SQL review, updated model/history tests, fresh installation and upgrade tests, singleton/UTC tests, and disposable deletion-independence verification.

The [implementation and checks](../development/bootstrap-completion-storage.md) distinguish this storage slice from the pending atomic account/claim/marker creation, operator permissions, and console workflow. No recovery/reset behavior is authorized by the schema approval.

Related: [permanent-completion rule](0027-permanent-bootstrap-completion.md), [decision index](README.md).
