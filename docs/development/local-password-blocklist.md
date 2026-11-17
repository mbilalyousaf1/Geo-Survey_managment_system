# Local bootstrap password blocklist

Implementation date: 2026-11-16.

The approved local corpus/checker is implemented in Identity Infrastructure. It performs accepted base normalization/validation plus exact local-corpus rejection, without making HTTP requests or creating accounts. It is not registered in the API or wired to a bootstrap command yet.

## Source and inspection

| Item | Recorded value |
| --- | --- |
| Source | SecLists `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt` |
| Immutable upstream commit | `4d5e414c9631f3c12ee1ac86ff7234d848391fd3` |
| Retrieval date | 2026-11-16 |
| Bytes | 8557632 |
| SHA-256 | `424A3E03A17DF0A2BC2B3CA749D81B04E79D59CB7AEEC8876A5A3F308D0CAF51` |
| Encoding | Strict UTF-8; no invalid entries observed |
| Records | 1000000 |
| Unique nonempty NFC entries | 999996 |
| Duplicates after NFC | 3 |
| Empty records | 1 |
| Values changed by NFC | 0 |

The original downloaded bytes remain unchanged under `PasswordBlocklistData/common-passwords.txt`. The upstream published MIT notice is retained as `LICENSE.txt`; `provenance.json` records the immutable source/license URLs, checksum, date, statistics, and processing. Only the approved published password corpus is imported: no account/email pairs or developer secret stores are read. The source is a finite common-password list, not exhaustive compromised-password detection.

`.gitattributes` disables text conversion and line-by-line diff for the large raw corpus. `.editorconfig` prevents automatic charset/whitespace/newline rewriting of that file. These protect the checksum and meaningful spaces; ordinary source formatting remains enforced.

## Packaging and important methods

`LocalPasswordBlocklist.LoadPackaged()` lazily loads the assembly's embedded corpus once per process, using the expected checksum compiled into the application. It caches a successful immutable instance or a load failure; restart the process after deploying a corrected snapshot. No mutable runtime auto-update or HTTP fallback exists.

`LoadVerified(Stream, expectedSha256)` is the narrow loader for trusted local deployment sources and deterministic tests. The expected digest must come from an independently reviewed trust anchor, not be computed from an untrusted file immediately before loading it. The loader leaves the caller's stream open and reads from its current position. The packaged path owns/disposes its embedded stream.

Loading follows this order:

1. Validate the expected SHA-256 format and readable stream; read at most 32 MiB as a resource-safety limit.
2. Verify the raw bytes' digest before interpreting them.
3. Decode UTF-8 strictly, supporting an optional UTF-8 BOM and ordinary CR/LF line framing.
4. Count and skip empty records, which base length validation always rejects. Normalize nonempty entries to NFC, preserve case/whitespace, and count/deduplicate ordinally. Do not length-filter the source or silently discard malformed content.
5. Reject zero-byte/empty-only corpora. Build a private immutable `FrozenSet<string>` for lookup.

Missing packaged resource, unreadable/failed streams, bad checksum, invalid UTF-8, empty corpus, and oversize input prevent obtaining a usable checker. Operational errors use a fixed exception message without source contents/paths or attached parser exceptions; failure is never returned as a negative match. Bad expected-digest arguments also throw rather than produce a permissive checker.

`NormalizeAndValidate(password)` first invokes `BootstrapPassword.NormalizeAndValidateBase`, then checks the entire normalized value against the frozen corpus. A match raises a generic choose-another-password error without echoing input; otherwise it returns the normalized string for the future reviewed hashing path. Candidate passwords are not inserted into the corpus or persisted.

`PasswordCorpusStatistics` exposes counts only. Its generated representation contains no corpus or candidate values. The 32 MiB limit is an implementation resource bound, not a password rule; a future larger source requires review of packaging/memory limits. Loading roughly one million entries consumes more memory than the 8.16 MiB raw file, so the loader is lazy and is not invoked per HTTP request.

The checker preserves exact ordinal semantics: case or whitespace variants are different unless separately present in the corpus; substrings are not rejected. Passing this check is not a guarantee against guessing or every breach, and grants no authorization. Operator authorization, atomic bootstrap, MFA, and future credential-change flows still require implementation.

## Publication and attribution

The raw corpus is an embedded resource, not a runtime download or a second loose published plaintext file. The Identity project copies its published license and provenance to `ThirdPartyNotices/SecLists-LICENSE.txt` and `ThirdPartyNotices/SecLists-provenance.json` for consumers. API publish verification confirmed the Identity assembly and both notices are present. Retain these notices when distributing the artifact. No new package, schema, endpoint, or authentication service registration is introduced.

## Reproduction and controlled updates

Current immutable source URLs are recorded in `PasswordBlocklistData/provenance.json`. To reproduce or review a candidate update, download it to a new temporary review directory rather than overwrite the retained snapshot. Verify its checksum and strict encoding; inspect counts, normalization, blank/duplicate handling, size, provenance, and license changes without printing password entries into logs.

For an approved upstream update:

1. Select an immutable upstream commit and review the dataset/license at that revision. Do not use a moving branch URL in a released artifact.
2. Download candidate bytes and notice into a new review directory; retain/review retrieval metadata and calculate the SHA-256 digest. Treat source changes as data-supply changes requiring review, not an automatic startup task.
3. Inspect/compare counts and known-match tests. If encoding is invalid, the loader limit is exceeded, or contents are unexpected, stop for review; do not discard records to force success.
4. Replace the raw snapshot/notice deliberately and update `PackagedCorpusSha256`, provenance, packaged-count expectations, and these notes together. Preserve raw bytes under the Git/editor attributes.
5. Run the build, unit/full-backend checks, formatting, checksum/attribute review, and API publish checks. Review the diff/provenance before a separately authorized commit/deployment.
6. Deploy the reviewed new artifact and restart the consuming process. Do not fetch/update the list during password submission or API startup.

SHA-256 here verifies corpus integrity; it is not the password-storage hash. Password hashing continues to use the separately accepted Identity PBKDF2 settings. A compromised deployment capable of replacing both application code and embedded data is outside the checksum's protection.

## Verification

Commands used for this slice:

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests/GeoSurvey.Identity.Infrastructure.Tests.csproj --configuration Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Backend.ps1
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
git check-attr text diff -- src/Backend/Modules/Identity/GeoSurvey.Identity.Infrastructure/PasswordBlocklistData/common-passwords.txt
Get-FileHash -Algorithm SHA256 src/Backend/Modules/Identity/GeoSurvey.Identity.Infrastructure/PasswordBlocklistData/common-passwords.txt
```

API publication used `dotnet publish src/Backend/GeoSurvey.Api/GeoSurvey.Api.csproj --configuration Release --no-restore --output <new TestResults/blocklist-publish-GUID directory>`, then checked the assembly and two notice files. The artifact directory is ignored and no existing output directory was replaced.

The first Identity test run reported five failures because InvalidDataException was not included in the operational-error filter. The filter was corrected; the subsequent Identity run passed all 82 cases, including 20 new blocklist cases. These cover real packaged counts/matching/attribution, Unicode/case/whitespace/whole-value behavior, base constraints, checksum/encoding/size/stream failures, duplicates, and empty records. I/O failure simulation exists only in a test stream; production lookup uses the real retained corpus.

No operator command, actual credential/account write, API wiring, new package, or database migration was added. Hosted CI, Docker image rebuild, frontend, and mobile were not run for this slice. Nothing was committed or pushed.

Final local verification: Release build passed with zero warnings/errors; formatting passed; full backend runner passed 208 tests with zero failures/skips (Identity unit 82, Organization unit 38, Architecture 13, API 8, ControlPlane 25, Identity SQL Server 35, Organization SQL Server 7). API publish and notice-file checks passed. The retained SHA-256 remained unchanged after build/formatting, and Git reports both text conversion and textual diff unset for the raw corpus. No package/dependency restore was needed because only resource items were added to the project.

## Related documentation

- [Accepted corpus design](../architecture/local-password-blocklist-design.md)
- [Password base components](bootstrap-password-components.md)
- [Phase 1 remaining plan](phase-1-completion-plan.md)
