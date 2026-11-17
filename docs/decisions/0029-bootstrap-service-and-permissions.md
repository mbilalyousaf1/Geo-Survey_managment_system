# 0029: Bootstrap service and permissions

Status: Accepted

Accepted and recorded: 2026-11-17

The owner approved the transactional creation-service slice, using shared email/password/corpus/hashing rules before the lock; marker and administrator checks inside the lock; atomic account/claim/marker creation; no adoption/reset of existing accounts; a 30-second application-lock timeout and 45-second SQL-command timeout.

Use database role GeoSurveyBootstrap for operator membership and the application-lock principal. A separately reviewed security-administrator script grants SELECT/INSERT on Accounts, AccountClaims, and BootstrapCompletion and denies UPDATE/DELETE on the marker. No schema/security modification privileges or API runtime membership are granted. The script is executed only against disposable databases during testing, not real deployment databases.

Scope includes service/result types, operator permission SQL, SQL Server tests, and documentation. It excludes the console, real credential provisioning, a public endpoint, new packages/migrations, and automatic application startup actions. The [implementation notes](../development/bootstrap-creation-service.md) document actual permission limits, safe error/logging behavior, test impersonation, and commit-outcome limitations.

Related: [operator model](0026-bootstrap-console-and-operator-access.md), [permanent completion](0027-permanent-bootstrap-completion.md), [decision index](README.md).
