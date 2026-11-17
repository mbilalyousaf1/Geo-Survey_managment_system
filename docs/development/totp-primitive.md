# TOTP primitive

Approved design: [ADR 0034](../decisions/0034-totp-primitive.md).

TotpAuthenticator lives in Identity Infrastructure/Mfa. GenerateSecret uses the system cryptographic random generator and a bounded Base32 encoder. Verify decodes only the approved 32-character secret, checks exactly six ASCII digits, reads injected UTC time once, and computes candidates for current step minus one through plus one. The counter is encoded big-endian, HMAC-SHA1 is dynamically truncated to a positive 31-bit value, and modulo 1,000,000 yields six digits. Leading zeroes are retained. All candidate comparisons use CryptographicOperations.FixedTimeEquals; this does not claim the entire input-validation function is constant-time.

Verify returns a nullable long: null means invalid input/no match, while a number identifies the latest matching step. Selecting the latest step resolves rare matching-code collisions within the allowed window conservatively for later replay enforcement. The primitive is intentionally stateless; it does not persist the accepted step, count failures, consume a challenge, confirm a pending secret, update TwoFactorEnabled, or issue an access token. It is not registered in the API or wired to pending enrollment yet. Do not expose server-generated verification codes through an endpoint; only secret generation and verification are provided here.

No new interface was added: the concrete class has a single calculation responsibility and takes the existing TimeProvider abstraction. No additional service/dependency is needed solely to wrap it. Mutable buffers are cleared on success/failure; managed strings remain subject to normal memory lifetime. Never log secrets/codes or include them in routes, traces, or diagnostics.

## Verification

```powershell
dotnet build GeoSurvey.sln --configuration Release --no-restore
dotnet test tests/Unit/GeoSurvey.Identity.Infrastructure.Tests --configuration Release --no-build --no-restore
dotnet test tests/Architecture/GeoSurvey.ArchitectureTests --configuration Release --no-build --no-restore
dotnet format GeoSurvey.sln --verify-no-changes --no-restore
git diff --check
```

The Release build passed with zero warnings/errors. All 139 Identity unit cases passed, including 43 new TOTP cases. Published RFC 4226 vectors cover counters 0–9 using their equivalent 30-second time steps; six SHA1 RFC 6238 vectors are reduced from eight to six digits by modulo 10^6, including the year 2603. Other cases cover leading zeroes, exact and fractional-step boundaries, current/previous/next-step acceptance, rejection beyond the window, epoch handling, malformed/noncanonical inputs, generated-secret canonical encoding/interoperability, and explicit stateless repeat behavior. An independent test-only bit-string decoder/reference calculation supplements the published-vector tests for generated secrets; production helpers are not called to create those expected values.

All 13 architecture tests also passed with zero failures/skips. Full formatting/analyzer verification, documentation-link checks for the new notes/proposal, content/diff review, and Git whitespace checks passed. No build/test failure occurred during this slice. No restore was repeated because dependencies did not change. SQL/API/console integration tests were not repeated: this task changes a standalone primitive and its unit tests, with no persistence/API/host wiring changes. The preceding full backend run had 299 passing tests; that is historical evidence, not a full-suite result for this task. No production authenticator enrollment, physical authenticator app test, hosted CI, frontend, mobile, commit, or push was performed.

Next small review: persistent failed-attempt policy across challenge replacement, followed by atomic replay enforcement and enrollment confirmation with confirmed-secret/recovery-code storage. These controls must exist before public confirmation/login endpoints are enabled.
