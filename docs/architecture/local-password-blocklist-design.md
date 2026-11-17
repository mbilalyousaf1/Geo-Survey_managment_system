# Local bootstrap password blocklist

Status: Local approach accepted in [record 0024](../decisions/0024-local-password-corpus.md); source/matching/failure/update implementation slice accepted in [record 0025](../decisions/0025-local-password-blocklist-details.md), 2026-11-16. [Implementation and inspection results](../development/local-password-blocklist.md) are now recorded. Proposal wording below describes the design reviewed before implementation.

## Accepted source and provenance

Use SecLists' published `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt`, pinned to an explicitly reviewed immutable commit when implementing, rather than downloading the moving master version at runtime. Retain source URL, commit, SHA-256 checksum, retrieval date, and the project's published MIT notice/provenance with the packaged data. The immutable snapshot is now retained and embedded; see the implementation notes for its exact commit/checksum and inspection counts.

Sources reviewed: [published dataset](https://github.com/danielmiessler/SecLists/blob/master/Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt) and [project license](https://github.com/danielmiessler/SecLists/blob/master/LICENSE). A finite common-password corpus does not identify every compromised password and must not be described as exhaustive breach detection.

## Accepted behavior

- Normalize candidate and corpus values with the accepted NFC behavior. Compare complete values ordinally; preserve case and whitespace. No substring blocking or credential mutation is introduced.
- Package a validated read-only local snapshot with provenance; validate its expected checksum before use. Corpus inspection during implementation must report encoding, usable/duplicate/invalid entry counts, and any processing required before relying on it. Do not silently discard invalid data or substitute a tiny illustrative list.
- Missing, unreadable, empty, malformed, or checksum-invalid data blocks bootstrap credential creation with a safe operational error. It must not be interpreted as a password not being blocked.
- A blocked candidate returns a generic choose-another-password result without logging the candidate. Runtime validation makes no external HTTP request and sends no password-derived data to a provider.
- Keep source updates explicit and reviewable: select/pin an upstream revision, review license/provenance and data changes, regenerate/check checksums, run tests, then deploy the new snapshot. Do not automatically update the corpus on application startup.
- No secret store, password database, or actual user credentials are imported. Test fixtures are synthetic; the real production corpus is the reviewed external dataset, not test data.

## Implemented first slice

Identity Infrastructure now provides LocalPasswordBlocklist, a verified immutable embedded snapshot, safe statistics, and combined base-plus-corpus checking. Tests cover actual packaging/counts and deterministic matching/failure cases. Published notices, provenance, checksum, and controlled update instructions accompany the implementation. No account write, endpoint, command, new hashing algorithm, or migration was added.

Record 0025 captures the separate approval of the concrete source/behavior and implementation. It does not authorize operator commands, account writes, MFA, or other pending bootstrap decisions.

## References

- [Accepted base password policy](../decisions/0023-bootstrap-base-password-policy.md)
- [Phase 1 completion plan](../development/phase-1-completion-plan.md)
