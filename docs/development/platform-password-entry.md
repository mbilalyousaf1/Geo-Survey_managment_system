# Platform Administrator password entry

Approved behavior: [ADR 0037](../decisions/0037-platform-password-entry.md).

## Components

PlatformSignInPassword is a verification-only normalization helper. It preserves spaces/case, applies NFC, rejects missing or invalid Unicode, and counts the normalized maximum in Unicode code points rather than UTF-16 units. It deliberately permits existing short passwords without rerunning creation minimum/blocklist policy; it is not a credential-creation API.

IPlatformPasswordEntry defines the dedicated platform entry contract. PlatformPasswordEntryResult contains only ChallengeIssued, nullable Purpose, and nullable Challenge (opaque token/expiry). It is not a record and its default ToString does not print token data. A rejected result is the same empty singleton for all credential/eligibility failures. Never log/destructure a ticket-bearing result. A true issuance status is not MFA completion or an authenticated principal.

PlatformPasswordEntryService in ControlPlane Infrastructure takes trusted global-context options and TimeProvider. Each BeginAsync creates and disposes a private context and transaction. This is intentional because the reused issuer calls SaveChanges: caller-tracked entities cannot leak into the entry transaction. Use the application's existing connection-string-based context options; do not supply a shared caller-owned connection/transaction. The service is not registered in the API yet.

The service looks up normalized email only in Platform scope with the administrator claim. It verifies the stored snapshot hash using the existing 220000-iteration Identity V3 configuration before holding the SQL update lock. After acquiring the account base-row lock it rereads eligibility and checks the exact hash/security stamp and normalized email. A concurrent hash/stamp/email/claim change discards stale proof without changing the new account's counters. Hash verification is framework code, not a new password-hashing algorithm.

Missing/noneligible accounts receive actual dummy hash work against a process-local random hash. The dummy account object is never persisted, selected as a login subject, or allowed to issue a challenge. Malformed base64 hashes are rejected safely. Other framework-rejected hash payloads remain ordinary failed verification outcomes. No wall-clock timing equality is promised: older hash costs, first-use dummy initialization, locks, and SQL access differ. Per-request/IP/identifier throttling still needs its public API design.

## Password and MFA state

Five consecutive wrong passwords cause a 15-minute lockout and revoke outstanding MFA challenges. The consecutive count does not expire after 15 minutes when no lockout has occurred. Active dates are not extended, and correct passwords cannot bypass them. A legacy false lockout flag with an active date is enabled without clearing its date; its challenges are revoked. After expiry a wrong password starts at one and clears the expired date. Counts from legacy invalid ranges are bounded before increment to avoid overflow. Correct password verification clears the password counter/date and establishes LockoutEnabled for the platform policy.

Identity V2/older-cost V3 hashes that verify are rehashed with current settings. That update, security/concurrency stamps, old-challenge revocation, and any new challenge persist atomically. Modern verified hashes retain their security stamp. MFA counts, failure window, block timestamp, accepted step, credential, and recovery rows are untouched. Correct password state can commit with a generic rejection when MFA is blocked or inconsistent; that never removes the MFA block or repairs credential state.

If MFA is disabled, confirmed/recovery rows must both be absent before an enrollment challenge can be issued. If MFA is enabled, confirmed credential and issued recovery data must both exist before a verification challenge can be issued. All recovery codes may already be consumed; their exhaustion alone does not disable authenticator login. Ciphertext validity is checked by the later MFA verification flow, not by password entry. Prior same-purpose challenges are replaced by the existing issuer. TryIssueAsync returns null for normal eligibility rejection while IssueAsync retains its prior throwing contract.

The private context disables inherited diagnostic sinks and sensitive data logging. SQL/update failures raise one fixed infrastructure error without inner provider details. Wrong-password outcomes commit their counters; exceptions before commit roll back all changes. New tickets are returned only after commit. No transparent retry is added; future callers must account for uncertain commit outcomes and follow normal fresh password-entry behavior rather than trust an unreturned nonce.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Release builds passed with zero warnings/errors. The full backend run passed 453 tests, zero failures/skips: ControlPlane SQL 158, Identity unit 159, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 19. This adds 36 password-entry SQL cases and eight Identity unit cases. Formatting/analyzers, new-document link review, code/diff review, and Git whitespace checks passed. The first targeted unit run passed 158 and failed one new reflection-based result-shape assertion because it included the static Rejected factory alongside instance properties. Restricting the assertion to public instance properties fixed the test; the subsequent full run included the corrected assertion. No dependency or schema changed, so restore/migration generation was not repeated; existing migration/snapshot tests ran in the backend suite.

New SQL cases use disposable SQL Server databases and real password hashes. Coverage includes scoped email reuse, organization/nonadministrator isolation, Unicode/whitespace, existing short credentials, enrollment/confirmed-factor purposes, consumed backups, generic rejection, password threshold/expiry/no-extension/consecutive counting, legacy flag handling, independent MFA state, inconsistent credential state, V2/V3 upgrades, missing/malformed hashes, concurrent failures/successes, and deterministic post-hash credential/claim changes. A challenge-write failure tests rollback of count reset, rehash/stamps, and old-nonce revocation. A real injected SQL error verifies safe diagnostics even with inherited sensitive logging enabled. Pre-cancellation is also checked.

No real administrator, public endpoint, authenticated session, JWT/cookie/refresh token, host registration, request throttling, tenant routing, deployment, hosted CI, frontend/mobile, commit, or push is included. The future API must resolve the account from trusted stored challenge data and design its throttling before exposing this flow. Next small review: confirmed-factor verification and the session/authorization contract, with recovery-code consumption/authorization still separately unresolved.
