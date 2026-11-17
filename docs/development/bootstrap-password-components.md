# Bootstrap base password components

Implementation and verification date: 2026-11-16.

Subsequent slice: the [verified local blocklist](local-password-blocklist.md) now supplies the approved corpus check in addition to these base rules. The original base-component boundaries/results below remain historical; the base method alone still does not perform blocklist validation or authorize account creation.

This slice implements the accepted normalization/base-length checks and shared Identity hashing settings. It is not a complete password-strength decision: the common/compromised-password blocklist remains unresolved. No real bootstrap account creation is enabled.

## Important methods

`BootstrapPassword.NormalizeAndValidateBase(string password)` in Identity Infrastructure:

1. Rejects null and malformed Unicode without echoing input or retaining a parser exception that might expose it.
2. Applies NFC normalization using .NET's standard normalization API.
3. Enumerates Unicode runes and requires 15–128 code points after normalization. A supplementary character such as an emoji counts once even though .NET stores it as two UTF-16 code units. A decomposed accent may compose into one code point before counting.
4. Returns the normalized string without trimming, case folding, or truncation. Whitespace is significant. There is no password wrapper/record with a printable generated representation.

The method name explicitly includes `Base`: even a sufficiently long repeated/whitespace-only input can pass this limited component. It must not be treated as a fully approved bootstrap password before the required blocklist and complete creation workflow exist. Do not log the input or returned string. Managed strings remain sensitive in process memory; returning a string does not provide memory erasure guarantees.

`IdentityPasswordHashingOptions.Configure(PasswordHasherOptions options)` selects Identity V3 and 220000 iterations. It configures the existing framework hasher; it does not implement a new hash algorithm or change database schema. The pinned framework format uses PBKDF2-HMAC-SHA512, a random 128-bit salt, and 256-bit subkey, with parameters encoded into the hash.

`IPasswordHasher<IdentityAccount>` is the existing framework contract: callers can request hashing/verification without depending on the concrete hasher. `PasswordHasher<IdentityAccount>` implements it and manages salt generation and encoding. Hashing itself does not enforce password policy, normalize Unicode, or persist account changes. Future approved creation and verification paths must apply the shared normalization/settings consistently. A rehash indication is a result to handle, not automatic permission for bootstrap to modify an existing account.

These components introduce no packages, custom interfaces, DI registration, expiry service, API endpoint, account writes, or migration. They are not wired into the API. No organization-user policy is selected, and Identity's default user-manager password validators are not configured by this slice.

## Tests

Twenty new normalization/base-validation cases verify ASCII and Unicode boundaries, NFC composition before counting, supplementary code points, null/invalid Unicode, input-safe errors, whitespace/case preservation, and absence of composition requirements.

Seven hashing tests exercise the real framework hasher: distinct salted hashes for the same input, correct/wrong-password verification, approved encoded parameters, canonical Unicode equivalence when both paths normalize, whitespace significance, and SuccessRehashNeeded for a lower work factor without account mutation. Hash-format inspection is test-only; production code does not parse or implement its own crypto format. The timing diagnostic reports elapsed cost only and imposes no flaky timing threshold. All test values are synthetic, not default credentials.

## Verification commands and results

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests/GeoSurvey.Identity.Infrastructure.Tests.csproj --configuration Release --no-build --no-restore --logger 'console;verbosity=detailed'
dotnet test tests/Architecture/GeoSurvey.ArchitectureTests/GeoSurvey.ArchitectureTests.csproj --configuration Release --no-build --no-restore
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Build passed with zero warnings/errors. Identity unit tests: 62 passed, including 27 new cases; architecture tests: 13 passed. No failures or skips. Formatting passed. These are the checks executed for this slice; the entire backend suite was not rerun. No restore was needed because project/package dependencies did not change.

The Windows .NET 10.0.9 local diagnostic used one warm-up hash/verification followed by three measured pairs: mean hashing 193.4 ms, mean verification 215.1 ms at 220000 iterations. Other work, including formatting verification, was running concurrently. This small sample is not a deployment benchmark, throughput estimate, or performance guarantee. It printed neither passwords nor hashes; deployment hardware needs its own measurement.

SQL integration/API runtime tests, Docker, hosted CI, frontend, and mobile were not rerun because this slice changes no persistence, HTTP registration, or container setup. No database or actual user credentials were touched. Nothing was committed or pushed.

## Remaining work

Review a blocklist source, licensing/update process, comparison behavior, and failure handling before enabling actual bootstrap creation. Operator authorization, command packaging, transactional bootstrap integration, MFA enrollment/session behavior, and recovery remain separate approved tasks. The transaction/lock design is accepted but is not implemented here.

## References

- [Accepted base policy](../decisions/0023-bootstrap-base-password-policy.md)
- [Password policy design and sources](../architecture/bootstrap-password-policy-proposal.md)
- [Bootstrap transaction/lock](../decisions/0022-bootstrap-transaction-and-lock.md)
