# 0032: API Data Protection host configuration

Status: Accepted, 2026-11-17.

The owner authorized host configuration after the account-bound protector and selected a mounted PFX file with its password supplied by secret configuration. The API now requires an explicit stable, environment-specific application name, existing absolute external key-ring directory, and existing absolute external certificate path. Startup rejects missing settings, an unreadable PFX, incorrect password, missing RSA private key, or a certificate outside its validity interval.

Use public Data Protection APIs for application isolation, filesystem persistence, and certificate encryption/decryption. A host-owned adapter creates a dedicated framework service provider lazily after configuration is available. This isolates key services, permits the loaded PFX to be passed through supported public APIs, and disposes those services with the host. The host separately owns/disposes the certificate; private-key loading is ephemeral in memory, while the Data Protection key ring remains persistent on disk. No cryptographic implementation or new package is added.

Before serving requests, verify directory write access, reject malformed/plaintext key XML, open non-revoked existing keys, and perform a protection round trip. Empty directories receive their first certificate-encrypted key during this check. Failure blocks startup; there is no ephemeral/unencrypted fallback. Fixed application exceptions omit supplied values and inner cryptographic/file diagnostics. The dedicated provider uses no framework logging sink; do not add configuration/secret dumps to host logs.

External-path validation rejects lexical paths beneath the application content root or a discoverable ancestor Git repository. Operators remain responsible for mount targets, symbolic links, filesystem permissions, and keeping PFX/private keys outside source/images. No real certificate, file permission, or deployment volume is provisioned by this task.

The API now requires this configuration even for liveness startup. Tests supply isolated generated certificates. The bootstrap console remains separate and does not enroll MFA. Certificate rotation/decryption of keys under additional historical certificates, backup/restore procedures, real deployment permissions, and Linux container runtime verification remain separate work. Keep the current certificate/private key and key-ring backups; replacing the PFX alone can block startup and lose access to existing secrets. Enrollment remains unimplemented.

See [operator runbook and evidence](../development/data-protection-host.md).
