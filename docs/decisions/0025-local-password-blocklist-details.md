# 0025: Local password blocklist source and operations

Status: Accepted

Accepted and recorded: 2026-11-16

Following the local-corpus choice in [record 0024](0024-local-password-corpus.md), the owner approved the SecLists common-password source, complete NFC-normalized ordinal matching, fail-closed integrity/loading behavior, reviewed pinned updates, and the loader/checker implementation slice.

Use a reviewed immutable revision of `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt`; retain provenance, SHA-256 checksum, retrieval date, and published license notice. Validate locally without an HTTP dependency. Missing/unusable/malformed/checksum-invalid data must block bootstrap credential creation rather than act like an empty blocklist. Preserve case and whitespace; compare complete values, not substrings.

The [implementation notes](../development/local-password-blocklist.md) record the pinned revision, raw-byte packaging, inspection results, immutable lazy loader, tests, and controlled update procedure. Data with malformed encoding is not silently discarded. The observed blank record is counted/skipped because base validation already rejects empty passwords; normalized duplicates are counted/deduplicated. No account creation or API registration is included.

This decision does not claim exhaustive breach detection or resolve operator authorization, MFA, bootstrap transaction implementation, or later credential-change workflows.

Related: [design](../architecture/local-password-blocklist-design.md), [base policy](0023-bootstrap-base-password-policy.md), [decision index](README.md).
