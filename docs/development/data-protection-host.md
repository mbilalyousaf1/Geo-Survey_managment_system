# API persistent Data Protection keys

Decision: [0032](../decisions/0032-api-data-protection-host.md). The API host now registers `ITotpSecretProtector` and requires persistent certificate-protected keys before it can serve requests. No enrollment endpoint or real authenticator storage is enabled.

## Required configuration

| Configuration key | Environment variable | Meaning |
| --- | --- | --- |
| `DataProtection:ApplicationName` | `DataProtection__ApplicationName` | Stable environment-specific discriminator, identical across replicas sharing secrets |
| `DataProtection:KeyRingPath` | `DataProtection__KeyRingPath` | Existing absolute directory on durable storage, writable by the API identity |
| `DataProtection:CertificatePath` | `DataProtection__CertificatePath` | Existing absolute mounted PFX file, readable by the API identity |
| `DataProtection:CertificatePassword` | `DataProtection__CertificatePassword` | Nonempty PFX password from a deployment secret or development user-secrets |

The existing `ConnectionStrings:GlobalControlPlane` requirement also remains. Keep all actual values in local environment/user-secrets or the deployment secret mechanism. Do not commit a PFX, private key, key XML, password, or real configuration. Docker must mount the key-ring volume persistently and the PFX read-only outside the application directory; do not COPY them into the image. This task does not generate a real certificate or change Compose/volume/permission settings.

Paths must be outside the application content root and any ancestor repository detected through `.git`. Checks are lexical; operators must also verify real mount/symlink targets and ACLs. The application does not grant itself permissions or create the configured directory. The PFX must contain a currently valid RSA private key. Trust-chain/hostname validation is not performed because this certificate wraps local keys rather than authenticating a TLS peer; certificate issuance and custody remain operator responsibilities.

## Lifecycle and failures

`AddGeoSurveyDataProtection` binds and validates settings. `HostCertificate` loads and owns the PFX using ephemeral private-key storage, avoiding a persistent machine-key installation. `HostProtectionProvider` owns dedicated framework services configured through `SetApplicationName`, `PersistKeysToFileSystem`, and `ProtectKeysWithCertificate`. The private provider is intentional: configuration is resolved lazily, and its lifecycle ends with the API host.

`KeyRingStartupCheck` runs before requests are served. It creates/removes a uniquely named empty write probe, inspects XML for malformed or plaintext keys, opens every non-revoked key, and performs an encryption/decryption round trip with synthetic data. A fresh ring creates its first encrypted key file. An unavailable private key or inaccessible ring blocks startup with a fixed safe error. A second provider/host can recover persisted data with the same ring, certificate, application discriminator, account, and purpose. Liveness therefore requires valid key setup but still does not establish SQL readiness.

Application name and purpose changes invalidate access to earlier protected payloads unless a compatibility plan is implemented. Preserve the PFX/private key and ring together in secure backups. Existing expired Data Protection keys may still be required for decryption; do not delete them merely because they are no longer used for encryption. Certificate expiration is rejected at startup. Do not replace the PFX as an improvised rotation procedure: historical-certificate support and rotation/re-encryption require another reviewed task. Real backup restoration, OS access-denial handling, Linux mounts, and deployment rotation have not been verified here.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Integration/GeoSurvey.Api.IntegrationTests --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

The targeted API run passed 19 tests (11 new cases). Coverage includes restart persistence across independent host/provider instances, encrypted key XML, application isolation, invalid settings/password, missing private key, malformed/plaintext key XML, repository paths, and wrong certificate for an existing ring. Test certificates/key files are generated under unique temporary directories and removed after use. Restart tests recreate providers in the test process; a real process/container restart is not exercised.

Final verification, 2026-11-17: Release build passed with zero warnings/errors. The full backend run passed 279 tests, zero failures/skips: API 19, Identity unit 93, ControlPlane SQL 50, Identity SQL 35, Organization SQL 7, Organization unit 38, console unit 24, and architecture 13. Full formatting/analyzer verification and Git whitespace checks passed.

The initial build referenced an internal framework configuration type; the implementation was corrected to use public builder APIs in an owned provider. Initial API fixture disposal attempted cleanup twice; idempotent cleanup fixed the reported class-cleanup failures. Subsequent targeted and full runs had no failures or cleanup errors. No package dependencies changed, so restore was not repeated.

No real deployment, credential provisioning, commit/push, hosted CI, frontend, or mobile check was performed. Next small task: review protected authenticator-secret persistence and pending-enrollment lifecycle before implementing enrollment.
