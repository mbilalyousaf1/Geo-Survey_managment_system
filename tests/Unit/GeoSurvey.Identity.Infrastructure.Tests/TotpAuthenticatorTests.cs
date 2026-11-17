using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure.Mfa;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class TotpAuthenticatorTests
{
    // RFC 4226 Appendix D and RFC 6238 Appendix B use the ASCII secret 12345678901234567890.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private sealed class Clock(long seconds) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(seconds);
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Now;
        }
    }

    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Rfc4226PublishedSixDigitValues_MatchTheirTimeSteps(long step, string code)
    {
        var authenticator = new TotpAuthenticator(new Clock(step * 30));
        Assert.Equal(step, authenticator.Verify(RfcSecret, code));
    }

    [Theory]
    // SHA1 eight-digit RFC 6238 vectors reduced modulo 10^6 for the approved six-digit format.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Rfc6238PublishedSha1Values_MatchIncludingBeyond2038(long seconds, string code)
    {
        Assert.Equal(seconds / 30, new TotpAuthenticator(new Clock(seconds)).Verify(RfcSecret, code));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(90, true)]
    [InlineData(119, true)]
    [InlineData(120, false)]
    public void AllowedWindow_IncludesExactlyOneStepEitherSide(long seconds, bool accepted)
    {
        // Published step-two code. At seconds 30..119, step two is within current +/- one.
        var result = new TotpAuthenticator(new Clock(seconds)).Verify(RfcSecret, "359152");
        Assert.Equal(accepted ? 2L : (long?)null, result);
    }

    [Fact]
    public void FractionalSecondBoundary_UsesFloorAndOneClockReading()
    {
        var clock = new Clock(120) { Now = DateTimeOffset.FromUnixTimeSeconds(120).AddTicks(-1) };
        var authenticator = new TotpAuthenticator(clock);
        Assert.Equal(2L, authenticator.Verify(RfcSecret, "359152"));
        Assert.Equal(1, clock.Reads);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(120);
        Assert.Null(authenticator.Verify(RfcSecret, "359152"));
        Assert.Equal(2, clock.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("81804")]
    [InlineData(" 081804")]
    [InlineData("081804 ")]
    [InlineData("+81804")]
    [InlineData("０８１８０４")]
    [InlineData("٠٨١٨٠٤")]
    [InlineData("081-804")]
    [InlineData("000000")]
    public void InvalidOrWrongCode_IsRejected(string? code)
    {
        Assert.Null(new TotpAuthenticator(new Clock(1111111109)).Verify(RfcSecret, code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GEZDGNBVGY3TQOJQ")]
    [InlineData("gezdgnbvgy3tqojqgezdgnbvgy3tqojq")]
    [InlineData("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJ=")]
    [InlineData("0EZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData(" GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    public void NonCanonicalSecret_IsRejected(string? secret)
    {
        Assert.Null(new TotpAuthenticator(new Clock(59)).Verify(secret, "287082"));
    }

    [Fact]
    public void BeforeEpoch_IsRejected_AndEpochDoesNotUnderflow()
    {
        Assert.Null(new TotpAuthenticator(new Clock(-1)).Verify(RfcSecret, "755224"));
        Assert.Equal(0L, new TotpAuthenticator(new Clock(0)).Verify(RfcSecret, "755224"));
    }

    [Fact]
    public void GeneratedSecrets_AreCanonicalDistinctAndInteroperateWithIndependentReference()
    {
        var values = Enumerable.Range(0, 32).Select(_ => TotpAuthenticator.GenerateSecret()).ToArray();
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
        foreach (var secret in values)
        {
            Assert.Equal(32, secret.Length);
            Assert.All(secret, character => Assert.True(character is >= 'A' and <= 'Z' or >= '2' and <= '7'));
            // Independent bit-string decode and allocation-based reference arithmetic; no production helpers.
            var bits = string.Concat(secret.Select(character => Convert.ToString("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(character), 2).PadLeft(5, '0')));
            var bytes = Enumerable.Range(0, 20).Select(index => Convert.ToByte(bits.Substring(index * 8, 8), 2)).ToArray();
            var counter = BitConverter.GetBytes(1234567890L / 30);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counter);
            var digest = HMACSHA1.HashData(bytes, counter);
            var offset = digest[19] & 15;
            var truncated = ((digest[offset] & 127) << 24) | (digest[offset + 1] << 16) | (digest[offset + 2] << 8) | digest[offset + 3];
            var code = (truncated % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(1234567890L / 30, new TotpAuthenticator(new Clock(1234567890)).Verify(secret, code));
        }
    }

    [Fact]
    public void RepeatedVerification_IsStatelessAndDoesNotClaimReplayEnforcement()
    {
        var authenticator = new TotpAuthenticator(new Clock(59));
        Assert.Equal(1L, authenticator.Verify(RfcSecret, "287082"));
        Assert.Equal(1L, authenticator.Verify(RfcSecret, "287082"));
    }
}
