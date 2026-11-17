# 0031: Account-bound TOTP secret protection

Status: Accepted, 2026-11-17.

TOTP requires the server to recover the shared authenticator secret to calculate expected codes. Hash-only storage cannot support that operation. Use ASP.NET Core Data Protection through `ITotpSecretProtector`, implemented by `DataProtectionTotpSecretProtector` in Identity Infrastructure.

Protection uses the purpose chain `GeoSurvey.Identity.TotpSecret.v1` followed by the account PublicId in N format. The account is selected by trusted server-side identity, never used as authorization merely because a client supplied it. Different accounts and purposes cannot share protected values. Preserve the purpose/version and account GUID format for existing ciphertext; future changes require an explicit compatibility/migration plan.

The host supplies `IDataProtectionProvider`. The approved deployment direction is a durable key ring outside source control and container images, encrypted with an operator-provided certificate. Restrict filesystem/private-key access to the application identity, isolate environments using the application discriminator and keys, preserve old decryption keys/certificates across rotation, and back up the required material securely. Losing it makes the secrets unrecoverable.

This slice implements the protector and tests only. It does not register a production provider, configure a key directory, create a certificate, write authenticator secrets to the database, or enable enrollment/login. Ephemeral providers are used only in tests. Deployment configuration and restart/rotation/backup validation remain prerequisites before persisted enrollment can be enabled.

Data Protection 10.0.15 is added. Existing Pkcs pins in ControlPlane Infrastructure and Identity SQL tests are aligned to 10.0.15 to avoid dependency downgrades. Earlier 10.0.9/10.0.10 attempts produced NuGet advisories; the final restore is clean. EF Core and Identity versions are unchanged.

See [implementation notes](../development/totp-secret-protection.md) and [Microsoft key-management guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
