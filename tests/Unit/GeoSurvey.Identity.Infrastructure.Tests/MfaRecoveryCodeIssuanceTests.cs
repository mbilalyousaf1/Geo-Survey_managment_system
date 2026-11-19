using GeoSurvey.Identity.Infrastructure.Mfa;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class MfaRecoveryCodeIssuanceTests
{
    private const string IssuedCode = "0000-1111-2222-3333-4444-5555-6666-7777";

    [Fact]
    public void Batch_ContainsTenDistinct128BitCodes_WithCanonicalDisplayAndReadOnlyCollection()
    {
        var first = MfaRecoveryCodes.GenerateBatch();
        var second = MfaRecoveryCodes.GenerateBatch();
        Assert.Equal(10, first.Count);
        Assert.Equal(20, first.Concat(second).Distinct(StringComparer.Ordinal).Count());
        foreach (var code in first.Concat(second))
        {
            Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){7}$", code);
            Assert.Equal(16, Convert.FromHexString(code.Replace("-", "", StringComparison.Ordinal)).Length);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first)[0] = IssuedCode);
    }

    [Fact]
    public void Digest_MatchesIndependentVector_AndIsBoundToAccountAndIssuedCode()
    {
        var account = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        // Independently calculated SHA-256 of purpose + NUL + account N + NUL + ungrouped hex.
        var digest = MfaRecoveryCodes.ComputeIssuedCodeDigest(account, IssuedCode);
        Assert.Equal("85F7F637B8DB5AC25F17E3EC6A3F3A0E8720FD77C4D59D39CF1C780333137F6D", digest);
        Assert.NotEqual(digest, MfaRecoveryCodes.ComputeIssuedCodeDigest(Guid.NewGuid(), IssuedCode));
        Assert.NotEqual(digest, MfaRecoveryCodes.ComputeIssuedCodeDigest(account, "0000-1111-2222-3333-4444-5555-6666-7778"));
        Assert.DoesNotContain(IssuedCode, digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00001111222233334444555566667777")]
    [InlineData("0000-1111-2222-3333-4444-5555-6666-777g")]
    [InlineData("0000-1111-2222-3333-4444-5555-6666-777a")]
    [InlineData("0000 1111-2222-3333-4444-5555-6666-7777")]
    public void Digest_RejectsNoncanonicalIssuedInputWithoutEchoingIt(string? input)
    {
        var error = Assert.Throws<ArgumentException>(() => MfaRecoveryCodes.ComputeIssuedCodeDigest(Guid.NewGuid(), input!));
        if (!string.IsNullOrEmpty(input))
            Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public void EmptyAccount_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => MfaRecoveryCodes.ComputeIssuedCodeDigest(Guid.Empty, IssuedCode));
    }

    [Fact]
    public void Result_DoesNotPrintCodes_AndDefensivelyCopiesItsBatch()
    {
        var codes = MfaRecoveryCodes.GenerateBatch().ToArray();
        var first = codes[0];
        var result = MfaEnrollmentConfirmationResult.Confirmed(codes);
        codes[0] = IssuedCode;
        Assert.True(result.Succeeded);
        Assert.Equal(first, result.RecoveryCodes[0]);
        Assert.All(result.RecoveryCodes, code => Assert.DoesNotContain(code, result.ToString()!));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.RecoveryCodes)[0] = IssuedCode);
        Assert.False(MfaEnrollmentConfirmationResult.Rejected.Succeeded);
        Assert.Empty(MfaEnrollmentConfirmationResult.Rejected.RecoveryCodes);
        Assert.Throws<ArgumentException>(() => MfaEnrollmentConfirmationResult.Confirmed(Array.Empty<string>()));
    }
}
