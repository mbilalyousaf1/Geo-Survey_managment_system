using System.Security.Cryptography;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class AccessTokenIssuanceTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string Issuer = "https://issuer.example.test";
    private const string Audience = "GeoSurvey.Tests";

    [Fact]
    public async Task SignedToken_ValidatesWithPublicKey_AndContainsOnlyPublicBindings()
    {
        using var rsa = RSA.Create(2048);
        using var publicRsa = RSA.Create();
        publicRsa.ImportParameters(rsa.ExportParameters(false));
        var subject = new SessionSubject(Guid.NewGuid(), Guid.NewGuid(), AuthenticationScope.Organization, Guid.NewGuid(), "synthetic@example.test");
        var issued = new RsaAccessTokenIssuer(new RsaSecurityKey(rsa), Issuer, Audience, new Clock(Now)).Issue(subject, Now.AddHours(8));
        var parameters = Validation(new RsaSecurityKey(publicRsa));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Value, parameters);
        Assert.True(validation.IsValid);
        var jwt = new JsonWebToken(issued.Value);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal("at+jwt", jwt.Typ);
        Assert.Equal(subject.AccountPublicId.ToString("D"), jwt.Subject);
        Assert.Equal(subject.SessionPublicId.ToString("D"), jwt.GetClaim("sid").Value);
        Assert.Equal(subject.OrganizationPublicId!.Value.ToString("D"), jwt.GetClaim("organizationId").Value);
        Assert.Equal("Organization", jwt.GetClaim("scope").Value);
        Assert.Equal(Now.AddMinutes(5), issued.ExpiresAtUtc);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type.Contains("stamp", StringComparison.OrdinalIgnoreCase)
            || claim.Type.Contains("database", StringComparison.OrdinalIgnoreCase) || claim.Type == "email");
        Assert.DoesNotContain(issued.Value, issued.ToString()!);
    }

    [Fact]
    public async Task ChangedSignatureIssuerAudienceAndExpiry_AreRejected()
    {
        using var rsa = RSA.Create(2048);
        using var wrong = RSA.Create(2048);
        var subject = new SessionSubject(Guid.NewGuid(), Guid.NewGuid(), AuthenticationScope.Platform, null, "synthetic@example.test");
        var token = new RsaAccessTokenIssuer(new RsaSecurityKey(rsa), Issuer, Audience, new Clock(Now)).Issue(subject, Now.AddHours(8));
        var parameters = Validation(new RsaSecurityKey(wrong));
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters)).IsValid);
        parameters = Validation(new RsaSecurityKey(rsa));
        parameters.ValidIssuer = "https://other.example.test";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters)).IsValid);
        parameters = Validation(new RsaSecurityKey(rsa));
        parameters.ValidAudience = "other";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters)).IsValid);
        parameters = Validation(new RsaSecurityKey(rsa));
        var expired = new RsaAccessTokenIssuer(new RsaSecurityKey(rsa), Issuer, Audience, new Clock(Now.AddMinutes(-10))).Issue(subject, Now.AddHours(8));
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(expired.Value, parameters)).IsValid);
    }

    [Fact]
    public void AccessExpiry_IsCappedByAbsoluteSessionDeadline()
    {
        using var rsa = RSA.Create(2048);
        var subject = new SessionSubject(Guid.NewGuid(), Guid.NewGuid(), AuthenticationScope.Platform, null, "synthetic@example.test");
        var issuer = new RsaAccessTokenIssuer(new RsaSecurityKey(rsa), Issuer, Audience, new Clock(Now));
        Assert.Equal(Now.AddSeconds(20), issuer.Issue(subject, Now.AddSeconds(20)).ExpiresAtUtc);
        Assert.Throws<InvalidOperationException>(() => issuer.Issue(subject, Now));
        Assert.Throws<ArgumentException>(() => issuer.Issue(subject with { OrganizationPublicId = Guid.NewGuid() }, Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => issuer.Issue(subject with { Scope = AuthenticationScope.Organization }, Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(" abcd-ef01-2345-6789-abcd-ef01-2345-6789 ", "ABCD-EF01-2345-6789-ABCD-EF01-2345-6789")]
    [InlineData("ABCDEFGHIJKLMNOP0123456789ABCDEF", null)]
    [InlineData("ABCD-EF01-2345-6789-ABCD-EF01-2345-678G", null)]
    [InlineData("ABCD EF01 2345 6789 ABCD EF01 2345 6789", null)]
    [InlineData(null, null)]
    public void RecoveryCodeNormalization_PreservesOnlyIssuedFormat(string? input, string? expected) =>
        Assert.Equal(expected, OpaqueAuthenticationToken.NormalizeRecoveryCode(input));

    private static TokenValidationParameters Validation(SecurityKey key) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        RequireSignedTokens = true,
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ValidTypes = ["at+jwt"]
    };
}
