# GeoSurvey Phase 1 submission

Delivery scope: local HTTPS Docker demonstration of Platform identity, organization provisioning, isolated tenant resolution, and initial Organization Administrator onboarding. This is the accepted [Phase 1 batch](../../development/phase-1-submission-delivery-plan.md); later GIS/survey and general RBAC features retain their own phases.

Delivery status, 2026-11-19: the approved local Phase 1 workflow is complete. Verification passed 631 backend tests, 24 frontend tests, the fresh real Docker/Chromium scenario (eleven checkpoints, zero skipped), formatting and clean extracted-source backend/frontend builds. See the exact evidence and remaining limitations below.

## Read and demonstrate

1. [Runbook and demonstration script](runbook.md).
2. [Architecture and schema overview](architecture-overview.md).
3. [Acceptance matrix](acceptance-matrix.md).
4. [Exact validation evidence](validation-results.md).
5. [Known limitations and production decisions](known-limitations.md).
6. [Parameterized Postman collection](GeoSurvey.Phase1.postman_collection.json).

The submission archive is generated with scripts/Package-Phase1Submission.ps1. It contains the current source worktree, migrations, tests, Dockerfiles, configuration-generation tooling, these documents, and a SHA-256 source manifest. It excludes private demo configuration, signing/Data Protection/HTTPS keys, runtime volumes, credentials, browser traces, generated dependency/build output, and Git metadata. No default administrator credentials or public bootstrap endpoint are supplied.

## Phase 1 behavior

The operator bootstraps a platform account using a masked console prompt. The web application enrolls real TOTP and displays ten recovery codes once. Password plus TOTP or an unused recovery code completes sign-in. Signed access tokens and rotated HttpOnly cookies are bound to current server-side account/session state.

An authorized Platform Administrator creates an organization in the local-demo region. A durable outbox and separate Hangfire worker create and migrate its independent SQL database, checking ownership and its immutable identity marker. A one-time 24-hour invitation lets the initial Organization Administrator set an independent password. Activation establishes the tenant grant before enabling the global credential and marking the organization Ready. Tenant requests derive the target from the signed account and trusted registry and recheck the marker/grant.

Five-minute access tokens, thirty-minute refresh inactivity, and eight-hour absolute sessions apply. The refresh window measures token use rather than human activity. Strict reuse detection revokes the family; current-session checks revoke API access without waiting for the access token to expire. Existing password/MFA attempt controls remain enforced.

This package is intended for the approved local submission. It does not claim production hosting, real data residency, a clean development-tool dependency audit, or features in later phases. See the limitations and actual validation results before review.
