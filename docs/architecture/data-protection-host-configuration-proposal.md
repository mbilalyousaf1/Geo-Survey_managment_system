# Data Protection host configuration proposal

Status: Implemented approved slice, 2026-11-17. The owner selected a mounted PFX with its password in secret configuration. See [accepted decision](../decisions/0032-api-data-protection-host.md) and [runbook](../development/data-protection-host.md). The original plan below is retained for context.

## Scope

Configure the API host to supply a durable Data Protection provider and register `ITotpSecretProtector`. The Identity module keeps ownership of account/purpose binding; the API composition root owns deployment configuration. No database migration, MFA enrollment endpoint, authenticator generation, or token issuance is included.

## Accepted certificate source

Use a mounted PFX file with its password supplied through secret configuration. A Windows certificate-store source is not included.

## Planned files and behavior

- Add an API configuration/registration class under `src/Backend/GeoSurvey.Api/Configuration`.
- Add registration/startup validation in `src/Backend/GeoSurvey.Api/Program.cs`.
- Add API integration tests and update API test-host setup with test-only certificate/key material where required.
- Update development/deployment documentation with configuration names and operator responsibilities after the concrete configuration contract is settled.

Require an explicit persistent external key-ring directory, a stable environment-specific application name, and a usable certificate/private key. Use Data Protection's certificate protection for persisted keys. Validation failures must give fixed actionable errors without echoing secrets or provider diagnostics. No implicit unencrypted or ephemeral fallback is permitted for the MFA protector.

The registration must own and dispose loaded certificate material with the host. Startup verification should exercise protection/unprotection without using a real authenticator secret. Document whether that verification creates a key-ring file. Do not provision real certificates or alter deployment permissions automatically.

## Verification plan

Use generated test certificates and isolated temporary directories. Verify new host/provider instances can decrypt previously protected material with the same ring, certificate, application name, account, and purpose. Verify key files are certificate-encrypted; reject missing/invalid configuration and unavailable private keys without leaking supplied values. Verify application isolation and failure with unavailable decryption material. Keep production rotation, backup restoration, and filesystem/private-key access verification explicitly separate from synthetic tests unless implemented in this slice.

See [accepted protection decision](../decisions/0031-totp-secret-protection.md) and [existing component](../development/totp-secret-protection.md).
