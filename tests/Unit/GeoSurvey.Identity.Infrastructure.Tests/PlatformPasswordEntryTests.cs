using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.SignIn;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class PlatformPasswordEntryTests
{
    [Fact]
    public void VerificationNormalization_PreservesCaseWhitespaceAndSupportsExistingShortPasswords()
    {
        Assert.Equal("  Existing pass é  ", PlatformSignInPassword.NormalizeForVerification("  Existing pass e\u0301  "));
        Assert.Equal("password", PlatformSignInPassword.NormalizeForVerification("password"));
        Assert.Equal(" ", PlatformSignInPassword.NormalizeForVerification(" "));
    }

    [Fact]
    public void Length_UsesNormalizedUnicodeCodePoints()
    {
        var supplementary = string.Concat(Enumerable.Repeat("\U0001F600", 128));
        Assert.Equal(supplementary, PlatformSignInPassword.NormalizeForVerification(supplementary));
        var decomposed = string.Concat(Enumerable.Repeat("e\u0301", 128));
        Assert.Equal(new string('é', 128), PlatformSignInPassword.NormalizeForVerification(decomposed));
        Assert.Throws<ArgumentException>(() => PlatformSignInPassword.NormalizeForVerification(supplementary + "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPassword_IsRejectedWithoutEchoingInput(string? input)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => PlatformSignInPassword.NormalizeForVerification(input!));
        if (!string.IsNullOrEmpty(input))
            Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public void InvalidUtf16_IsRejectedWithoutEchoingInput()
    {
        var input = new string('\uD800', 1);
        var error = Assert.Throws<ArgumentException>(() => PlatformSignInPassword.NormalizeForVerification(input));
        Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public void ChallengeResult_HasNoCredentialOrSessionData_AndDoesNotPrintToken()
    {
        var ticket = new MfaChallengeTicket(new string('a', 43), DateTimeOffset.UtcNow.AddMinutes(10));
        var result = PlatformPasswordEntryResult.Issued(MfaChallengePurpose.Enrollment, ticket);
        Assert.True(result.ChallengeIssued);
        Assert.Equal(MfaChallengePurpose.Enrollment, result.Purpose);
        Assert.Same(ticket, result.Challenge);
        Assert.DoesNotContain(ticket.Token, result.ToString()!);
        Assert.DoesNotContain(ticket.Token, ticket.ToString()!);
        Assert.Equal(new[] { "Challenge", "ChallengeIssued", "Purpose" }, typeof(PlatformPasswordEntryResult)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(property => property.Name).Order());
        Assert.False(PlatformPasswordEntryResult.Rejected.ChallengeIssued);
        Assert.Null(PlatformPasswordEntryResult.Rejected.Challenge);
        Assert.Null(PlatformPasswordEntryResult.Rejected.Purpose);
    }

    [Fact]
    public void InvalidPurposeOrTicket_CannotConstructIssuedResult()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformPasswordEntryResult.Issued((MfaChallengePurpose)99, new MfaChallengeTicket("synthetic", DateTimeOffset.UtcNow)));
        Assert.Throws<ArgumentNullException>(() => PlatformPasswordEntryResult.Issued(MfaChallengePurpose.Verification, null!));
    }

    private sealed class RejectingChallengeStore : IMfaChallengeStore
    {
        public Task<bool> ReplaceAsync(Guid accountPublicId, string verifiedSecurityStamp, MfaChallengePurpose purpose, string tokenHash,
            DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> IsValidAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> TryConsumeAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    [Fact]
    public async Task NonThrowingIssuance_ReturnsNoTicketOnNormalDenial_AndExistingIssuanceStillThrows()
    {
        var issuer = new MfaChallengeService(new RejectingChallengeStore(), TimeProvider.System);
        Assert.Null(await issuer.TryIssueAsync(Guid.NewGuid(), "synthetic-stamp", MfaChallengePurpose.Enrollment));
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(Guid.NewGuid(), "synthetic-stamp", MfaChallengePurpose.Enrollment));
    }
}
