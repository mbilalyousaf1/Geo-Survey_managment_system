# 0022: Bootstrap transaction and application lock

Status: Accepted

Accepted and recorded: 2026-11-16

## Decision

Serialize first-administrator bootstrap attempts using a transaction-owned SQL Server application lock in the global database:

1. Begin a database transaction.
2. Acquire one fixed bootstrap application lock using `sys.sp_getapplock`, exclusively, owned by that transaction.
3. Under the lock, check for an existing Platform Administrator using the accepted scope/claim representation.
4. If none exists, create the platform account with its password hash and administrator claim in the same transaction.
5. Commit together, or roll back on failure. If an administrator already exists, exit without changing credentials.

Use a 30-second application-lock timeout, represented as 30000 milliseconds. If acquisition fails, do not proceed with account creation; exit with a clear retry message without sensitive data. A waiting command must recheck existence after acquiring the lock.

The future operation belongs in ControlPlane Infrastructure behind a narrow bootstrap contract called by the operator command. No table or package is added by this design.

## Implementation constraints and limits

All bootstrap attempts must use the same database, lock resource, and principal. The lock is cooperative: it does not prevent arbitrary direct SQL writers or unreviewed account-creation paths. Exact lock resource naming and command/SQL timeout coordination remain implementation details to explain in the concrete code plan.

SQL Server requires an active transaction for transaction-owned locks and releases them at commit/rollback. Acquisition result codes must be checked; nonnegative codes indicate success. A failed/deadlocked acquisition must trigger explicit transaction cleanup, rather than assuming SQL Server rolled it back. See [Microsoft's application-lock documentation](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17).

No existence check may run as the deciding check outside the acquired lock. Account and claim persistence must share the transaction. Failure must not leave a partial administrator or reset credentials. Locking does not establish operator authorization, password policy, or completed MFA.

## Required verification with implementation

Use real SQL Server tests for concurrent attempts yielding exactly one administrator, complete rollback on failure, and rerun preserving credentials. Test lock acquisition failure/timeout and cleanup as relevant to the concrete implementation. No bootstrap concurrency test has been implemented or executed yet.

## Remaining work

Password policy/hashing integration, operator authorization, command packaging, MFA enrollment, recovery, and disabled/deleted administrator behavior remain unresolved as applicable. This approval does not authorize implementing those rules or generating the command without a concrete reviewed plan.

This task changes documentation only: no lock was acquired, account created, database changed, or code implemented.

## Related documentation

- [First administrator bootstrap](0007-first-platform-administrator-bootstrap.md)
- [Administrator claim](0021-platform-administrator-claim.md)
- [Mandatory MFA](0008-platform-administrator-mfa.md)
- [Decision index](README.md)
