# TOTP secret protection component

Approved design: [ADR 0031](../decisions/0031-totp-secret-protection.md).

`ITotpSecretProtector` defines the two operations needed by future enrollment/verification workflows. It keeps those callers independent of the particular encryption API. `DataProtectionTotpSecretProtector` implements the contract with an injected `IDataProtectionProvider`: the host owns key storage and configuration, while the component owns the versioned MFA purpose and account binding.

`Protect` rejects an empty account GUID or missing secret, then encrypts and authenticates the exact input. It deliberately does not generate, normalize, or validate TOTP parameters; those belong to the later enrollment design. `Unprotect` applies the same account/purpose chain. Cryptographic failures become a fixed `CryptographicException` without provider diagnostics or an inner exception. Callers must fail closed and must not log plaintext, ciphertext, provisioning URIs, or QR contents. Account binding supplements authorization; it does not replace it. Managed plaintext strings cannot be reliably erased by this component.

Tests use real Data Protection with ephemeral test-only key rings and a synthetic secret. They cover randomized round trips across wrapper instances sharing one provider, wrong accounts, unrelated purposes/keys, altered ciphertext, malformed/plaintext input, missing input, and empty GUIDs. No database or API changes are made.

## Deployment boundary

Before storing real secrets, configure the host with persistent keys on a protected external volume and certificate-based key encryption at rest. Use explicit environment isolation and stable application naming. Do not use an ephemeral provider or regenerate keys on container restart. Validate restart persistence, certificate/key rotation with old data, backup restoration, and denied access before enabling enrollment. No concrete deployment paths, certificates, cloud providers, or credentials are chosen here.

## Verification

```powershell
dotnet restore GeoSurvey.sln
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

Final restore succeeded without warnings; Release build succeeded with zero warnings/errors; all 93 Identity unit cases passed, including 11 new protector cases. The full backend run passed all 268 tests, zero failures/skips: Identity unit 93, ControlPlane SQL 50, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, architecture 13, and API 8. Full formatting/analyzer verification, documentation-link review for the two new notes, and Git whitespace checks passed. Initial restores exposed cryptography downgrade errors and advisories; resolving affected pins to 10.0.15 fixed them. The first build failed because a test referenced WebUtilities without a dependency; switching that helper to built-in System.Buffers.Text.Base64Url fixed it.

No real secret/certificate/key ring was provisioned. Production persistence, restart/rotation/backup behavior, hosted CI, frontend, and mobile checks are not exercised by this component task. No commit or push is performed.

Next small task: propose host Data Protection configuration and its failure behavior before wiring durable authenticator-secret storage.
