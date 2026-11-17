using System.Buffers.Text;
using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class TotpSecretProtectorTests
{
    // Test-only synthetic value; ephemeral keys must never be used for persisted production secrets.
    private const string Secret = "JBSWY3DPEHPK3PXP";

    [Fact]
    public void SameProviderAndAccount_RoundTripAcrossInstances_WithRandomizedCiphertext()
    {
        var provider = new EphemeralDataProtectionProvider();
        ITotpSecretProtector first = new DataProtectionTotpSecretProtector(provider);
        ITotpSecretProtector second = new DataProtectionTotpSecretProtector(provider);
        var account = Guid.NewGuid();
        var ciphertext = first.Protect(account, Secret);
        Assert.DoesNotContain(Secret, ciphertext);
        Assert.NotEqual(ciphertext, first.Protect(account, Secret));
        Assert.Equal(Secret, second.Unprotect(account, ciphertext));
    }

    [Fact]
    public void DifferentAccount_CannotDecrypt()
    {
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var ciphertext = protector.Protect(Guid.NewGuid(), Secret);
        AssertSafeFailure(() => protector.Unprotect(Guid.NewGuid(), ciphertext));
    }

    [Fact]
    public void DifferentKeyRing_CannotDecrypt()
    {
        var first = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var second = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var account = Guid.NewGuid();
        var ciphertext = first.Protect(account, Secret);
        AssertSafeFailure(() => second.Unprotect(account, ciphertext));
    }

    [Fact]
    public void DifferentPurpose_CannotDecrypt()
    {
        var provider = new EphemeralDataProtectionProvider();
        var account = Guid.NewGuid();
        var ciphertext = provider.CreateProtector("GeoSurvey.Identity.Other.v1", account.ToString("N")).Protect(Secret);
        var protector = new DataProtectionTotpSecretProtector(provider);
        AssertSafeFailure(() => protector.Unprotect(account, ciphertext));
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var account = Guid.NewGuid();
        var payload = Base64Url.DecodeFromChars(protector.Protect(account, Secret));
        payload[^1] ^= 1;
        AssertSafeFailure(() => protector.Unprotect(account, Base64Url.EncodeToString(payload)));
    }

    [Theory]
    [InlineData("not a protected value")]
    [InlineData(Secret)]
    public void MalformedOrPlaintextInput_IsRejected(string input)
    {
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        AssertSafeFailure(() => protector.Unprotect(Guid.NewGuid(), input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingInputs_AreRejected(string? input)
    {
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        Assert.ThrowsAny<ArgumentException>(() => protector.Protect(Guid.NewGuid(), input!));
        Assert.ThrowsAny<ArgumentException>(() => protector.Unprotect(Guid.NewGuid(), input!));
    }

    [Fact]
    public void EmptyAccount_IsRejected()
    {
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        Assert.Throws<ArgumentException>(() => protector.Protect(Guid.Empty, Secret));
        Assert.Throws<ArgumentException>(() => protector.Unprotect(Guid.Empty, "ciphertext"));
    }

    private static void AssertSafeFailure(Func<string> action)
    {
        var error = Assert.Throws<CryptographicException>(() => action());
        Assert.Equal("Authenticator secret could not be decrypted.", error.Message);
        Assert.Null(error.InnerException);
    }
}
