using GeoSurvey.Identity.Infrastructure.Sessions;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class SessionStorageModelTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Factories_CreateIndependentFamiliesAndCanonicalDigestOnlyStorage()
    {
        var first = TokenFamily.Create(1, "synthetic-stamp", Created, Created.AddHours(1));
        var second = TokenFamily.Create(1, "synthetic-stamp", Created, Created.AddHours(1));
        Assert.NotEqual(Guid.Empty, first.PublicId);
        Assert.NotEqual(first.PublicId, second.PublicId);
        Assert.Null(first.RevokedAtUtc);
        Assert.DoesNotContain("synthetic-stamp", first.ToString()!);

        var token = RefreshToken.Create(1L << 33, new string('a', 64), Created, Created.AddMinutes(5));
        Assert.Equal(1L << 33, token.TokenFamilyId);
        Assert.Equal(new string('A', 64), token.TokenHash);
        Assert.Null(token.ConsumedAtUtc);
        Assert.DoesNotContain(token.TokenHash, token.ToString()!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Factories_RejectNonpositiveInternalAssociations(int id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenFamily.Create(id, "stamp", Created, Created.AddHours(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RefreshToken.Create(id, new string('A', 64), Created, Created.AddMinutes(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Family_RequiresCredentialVersion(string? stamp)
    {
        Assert.ThrowsAny<ArgumentException>(() => TokenFamily.Create(1, stamp!, Created, Created.AddHours(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-sensitive-input")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public void Token_RejectsMalformedDigestWithoutEchoingInput(string? digest)
    {
        var error = Assert.Throws<ArgumentException>(() => RefreshToken.Create(1, digest!, Created, Created.AddMinutes(5)));
        if (!string.IsNullOrEmpty(digest))
            Assert.DoesNotContain(digest, error.Message);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void Token_RequiresFullSha256Digest(int length)
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(1, new string('A', length), Created, Created.AddMinutes(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Factories_RequireExpiryAfterCreation(int expirySeconds)
    {
        Assert.Throws<ArgumentException>(() => TokenFamily.Create(1, "stamp", Created, Created.AddSeconds(expirySeconds)));
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(1, new string('A', 64), Created, Created.AddSeconds(expirySeconds)));
    }

    [Theory]
    [InlineData(true, 180)]
    [InlineData(false, -60)]
    public void Factories_RejectNonzeroOffsetEvenForEquivalentUtcInstant(bool creation, int offsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var start = creation ? Created.ToOffset(offset) : Created;
        var expiry = creation ? Created.AddHours(1) : Created.AddHours(1).ToOffset(offset);
        Assert.Throws<ArgumentException>(() => TokenFamily.Create(1, "stamp", start, expiry));
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(1, new string('A', 64), start, expiry));
    }
}
