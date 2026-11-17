# 0024: Local maintained password corpus

Status: Accepted

Accepted and recorded: 2026-11-16

The product owner selected a local maintained corpus for bootstrap password blocking, with a reviewed source and update procedure, instead of a runtime online breached-password lookup. Password validation must not depend on network availability or send password data to an external service.

The specific corpus, matching rules, checksum/packaging format, update procedure, and failure policy are still proposed in the [local blocklist design](../architecture/local-password-blocklist-design.md). This approval selects the local approach only; no dataset or code has been added. Do not claim the existing base password component performs blocklist checks.

Related: [base password policy](0023-bootstrap-base-password-policy.md), [Phase 1 completion plan](../development/phase-1-completion-plan.md), [decision index](README.md).

Subsequent acceptance: [record 0025](0025-local-password-blocklist-details.md) resolves the source and operations and records the implementation slice. Earlier proposed/no-data statements describe this record's original scope.
