# Architecture decision records

These records capture the context, decision, and consequences of GeoSurvey's architecture choices. The [master specification](../../GeoSurvey_Master_Architecture_and_Codex_Spec.md) remains the source of truth. Recording an existing decision does not approve open business requirements or imply that all related code exists.

| Record | Status | Basis |
| --- | --- | --- |
| [0001: Modular monolith with Clean Architecture](0001-modular-monolith.md) | Accepted | Master specification ADR-001 and sections 3–4 |
| [0002: Separate mobile solution](0002-separate-mobile-solution.md) | Accepted | Approved Phase 0 mobile skeleton implementation |

Record numbers here identify documentation files. They are independent of the master specification's ADR identifiers: record 0002 does not replace master ADR-002, which covers request handling.

When a decision changes, add a new reviewed record and mark the earlier record as superseded with a link. Preserve the earlier reasoning. Use a Proposed status for choices awaiting approval; use Accepted only for choices already approved. An acceptance date records approval, while a recorded date indicates when the documentation was written.
