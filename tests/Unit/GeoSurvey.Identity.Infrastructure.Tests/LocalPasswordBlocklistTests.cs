using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class LocalPasswordBlocklistTests
{
    private static LocalPasswordBlocklist Load(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new MemoryStream(bytes);
        return LocalPasswordBlocklist.LoadVerified(stream, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void BlockedValue_IsRejectedWithoutInputEcho()
    {
        var blocklist = Load("blocked test passphrase\n");
        var exception = Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate("blocked test passphrase"));
        Assert.Contains("Choose a different password", exception.Message);
        Assert.DoesNotContain("blocked test passphrase", exception.ToString());
    }

    [Fact]
    public void CandidateAndCorpus_UseNfcAndWholeValueOrdinalMatching()
    {
        var blocklist = Load("example passphrase e\u0301\nblocked test passphrase\n");
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate("example passphrase \u00e9"));
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate("example passphrase e\u0301"));
        Assert.Equal("Blocked test passphrase", blocklist.NormalizeAndValidate("Blocked test passphrase"));
        Assert.Equal(" blocked test passphrase ", blocklist.NormalizeAndValidate(" blocked test passphrase "));
        Assert.Equal("extra blocked test passphrase suffix", blocklist.NormalizeAndValidate("extra blocked test passphrase suffix"));
        Assert.Equal("unlisted passphrase \u00e9", blocklist.NormalizeAndValidate("unlisted passphrase e\u0301"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too short")]
    public void CompleteCheck_PreservesBaseValidation(string? input)
    {
        var blocklist = Load("blocked test passphrase\n");
        Assert.ThrowsAny<ArgumentException>(() => blocklist.NormalizeAndValidate(input!));
    }

    [Fact]
    public void CompleteCheck_RejectsMalformedUnicodeAndOverlengthInput()
    {
        var blocklist = Load("blocked test passphrase\n");
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate("malformed input-\ud800"));
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate(new string('x', 129)));
    }

    [Fact]
    public void Statistics_ReportNormalizationDuplicatesAndEmptyRecords()
    {
        var blocklist = Load("example passphrase e\u0301\nexample passphrase \u00e9\n\nunique passphrase\n");
        Assert.Equal(new PasswordCorpusStatistics(4, 2, 1, 1, 1), blocklist.Statistics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\r\n")]
    public void EmptyOrOnlyEmptyRecords_FailClosed(string text)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(text));
        Assert.Contains("cannot continue", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void TamperedBytes_AreRejectedBeforeDecoding()
    {
        var bytes = Encoding.UTF8.GetBytes("do-not-echo-corpus-value");
        using var stream = new MemoryStream(bytes);
        var exception = Assert.Throws<InvalidOperationException>(() => LocalPasswordBlocklist.LoadVerified(stream, new string('0', 64)));
        Assert.DoesNotContain("do-not-echo", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void MalformedUtf8_FailsEvenWithMatchingDigest()
    {
        byte[] bytes = [0xc3, 0x28];
        using var stream = new MemoryStream(bytes);
        var exception = Assert.Throws<InvalidOperationException>(() => LocalPasswordBlocklist.LoadVerified(stream, Convert.ToHexString(SHA256.HashData(bytes))));
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad-digest")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000g")]
    public void MissingOrMalformedTrustedDigest_IsRejected(string? digest)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("blocked test passphrase"));
        Assert.Throws<ArgumentException>(() => LocalPasswordBlocklist.LoadVerified(stream, digest!));
    }

    [Fact]
    public void UnreadableStream_FailsClosed()
    {
        var stream = new MemoryStream();
        stream.Dispose();
        Assert.Throws<InvalidOperationException>(() => LocalPasswordBlocklist.LoadVerified(stream, new string('0', 64)));
    }

    [Fact]
    public void ReadFailure_DoesNotExposeSourceException()
    {
        using var stream = new FailingReadStream();
        var exception = Assert.Throws<InvalidOperationException>(() => LocalPasswordBlocklist.LoadVerified(stream, new string('0', 64)));
        Assert.DoesNotContain("do-not-echo", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void OversizedCorpus_FailsClosed()
    {
        using var stream = new MemoryStream(new byte[32 * 1024 * 1024 + 1]);
        Assert.Throws<InvalidOperationException>(() => LocalPasswordBlocklist.LoadVerified(stream, new string('0', 64)));
    }

    [Fact]
    public void VerifiedUtf8BomAndCrLf_AreSupportedWithoutTrimmingValues()
    {
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(" blocked test passphrase \r\n")).ToArray();
        using var stream = new MemoryStream(bytes);
        var blocklist = LocalPasswordBlocklist.LoadVerified(stream, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate(" blocked test passphrase "));
        Assert.Equal("blocked test passphrase", blocklist.NormalizeAndValidate("blocked test passphrase"));
    }

    [Fact]
    public void PackagedCorpus_IsVerifiedCachedAndHasReviewedCounts()
    {
        var blocklist = LocalPasswordBlocklist.LoadPackaged();
        Assert.Same(blocklist, LocalPasswordBlocklist.LoadPackaged());
        Assert.Equal(new PasswordCorpusStatistics(1000000, 999996, 3, 1, 0), blocklist.Statistics);
        // Known public blocklist value, never an application credential/default.
        Assert.Throws<ArgumentException>(() => blocklist.NormalizeAndValidate("passwordpassword"));
        Assert.Equal("GeoSurvey synthetic blocklist test phrase without real credentials",
            blocklist.NormalizeAndValidate("GeoSurvey synthetic blocklist test phrase without real credentials"));
    }

    [Fact]
    public void LicenseAndProvenance_AreCopiedForConsumers()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices");
        Assert.Contains("MIT License", File.ReadAllText(Path.Combine(directory, "SecLists-LICENSE.txt")));
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "SecLists-provenance.json")));
        Assert.Equal(LocalPasswordBlocklist.PackagedCorpusSha256, provenance.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(1000000, provenance.RootElement.GetProperty("records").GetInt32());
    }

    private sealed class FailingReadStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("do-not-echo-source-details");
    }
}
