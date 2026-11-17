# 0034: TOTP generation and verification primitive

Status: Accepted, 2026-11-17.

The owner approved the [TOTP proposal](../architecture/totp-verification-proposal.md). Add TotpAuthenticator to Identity Infrastructure without introducing a package, database change, API endpoint, or authentication grant.

Generate a fresh random 20-byte (160-bit) secret using RandomNumberGenerator, encoded as exactly 32 uppercase, unpadded Base32 characters. Verify HMAC-SHA1 six-digit ASCII codes with leading zeroes preserved, a Unix epoch origin, and 30-second intervals. Accept the current time step and one adjacent step in each direction. Reject noncanonical secrets and non-six-digit input rather than silently normalize it. Negative Unix time is invalid, and the step/counter uses 64-bit values.

Use .NET HMACSHA1 and fixed-time six-byte comparisons, with strict fixed-size Base32 encoding/decoding and RFC dynamic truncation. This is an approved small RFC implementation because the pinned framework AuthenticatorTokenProvider has a wider fixed window, no injected clock, and no matched-step result. It does not implement a new cryptographic hash primitive. Use TimeProvider and read it once per verification. Evaluate every valid candidate and return the latest matching step if rare adjacent-code collisions occur; never return a secret-bearing result object.

Zero mutable secret, digest, counter, and code buffers after use. Generated/received managed strings cannot be reliably erased; callers must not log them. A valid calculated code alone does not authenticate an account. This class stores no state, so repeated calls can return the same matched step. Future caller-side transactions must enforce replay/attempt limits, current account/challenge eligibility, and atomic consumption plus confirmed MFA state. No public MFA flow is enabled by this decision.

Verification uses published RFC 4226 six-digit vectors and the SHA1 RFC 6238 vectors reduced modulo 10^6 to match the approved digit count. Do not claim these tests exercise eight-digit or SHA256/SHA512 implementations, which are outside the selected parameters.

Sources: [RFC 4226](https://www.rfc-editor.org/rfc/rfc4226.html), [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238.html), [framework provider source](https://github.com/dotnet/aspnetcore/blob/v10.0.9/src/Identity/Extensions.Core/src/AuthenticatorTokenProvider.cs). See [implementation evidence](../development/totp-primitive.md).
