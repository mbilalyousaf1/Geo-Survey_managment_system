using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class PasswordHashingTests(ITestOutputHelper output)
{
    private static PasswordHasher<IdentityAccount> CreateHasher()
    {
        var options = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(options);
        return new PasswordHasher<IdentityAccount>(Options.Create(options));
    }

    private static IdentityAccount Account() => IdentityAccount.CreatePlatformAccount("hash-tests@example.test");

    [Fact]
    public void Hashes_AreSaltedVerifyCorrectInputAndRejectWrongInput()
    {
        var account = Account();
        var hasher = CreateHasher();
        var password = BootstrapPassword.NormalizeAndValidateBase("synthetic test passphrase");
        var first = hasher.HashPassword(account, password);
        var second = hasher.HashPassword(account, password);

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(account, first, password));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(account, second, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(account, first,
            BootstrapPassword.NormalizeAndValidateBase("different test passphrase")));
    }

    [Fact]
    public void EncodedHash_UsesApprovedV3Parameters()
    {
        var account = Account();
        var password = BootstrapPassword.NormalizeAndValidateBase("synthetic test passphrase");
        var encoded = Convert.FromBase64String(CreateHasher().HashPassword(account, password));

        // Inspect the pinned framework format without implementing our own hash/parser for production.
        Assert.Equal(1, encoded[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(1, 4))); // HMAC-SHA512
        Assert.Equal(220000u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(5, 4)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(9, 4)));
        Assert.Equal(13 + 16 + 32, encoded.Length);
    }

    [Fact]
    public void EquivalentUnicode_VerifiesAfterSameNormalization()
    {
        var account = Account();
        var hasher = CreateHasher();
        var hash = hasher.HashPassword(account, BootstrapPassword.NormalizeAndValidateBase("example passphrase e\u0301"));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(account, hash,
            BootstrapPassword.NormalizeAndValidateBase("example passphrase \u00e9")));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(account, hash, "example passphrase e\u0301"));
    }

    [Fact]
    public void Whitespace_IsSignificantForVerification()
    {
        var account = Account();
        var hasher = CreateHasher();
        var password = BootstrapPassword.NormalizeAndValidateBase("  synthetic test passphrase  ");
        var hash = hasher.HashPassword(account, password);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(account, hash, password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(account, hash, password.Trim()));
    }

    [Fact]
    public void OlderWorkFactor_ReturnsRehashIndicationWithoutChangingAccount()
    {
        var account = Account();
        var password = BootstrapPassword.NormalizeAndValidateBase("synthetic test passphrase");
        var oldHasher = new PasswordHasher<IdentityAccount>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 100000
        }));
        var oldHash = oldHasher.HashPassword(account, password);
        account.PasswordHash = oldHash;

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            CreateHasher().VerifyHashedPassword(account, oldHash, password));
        Assert.Equal(oldHash, account.PasswordHash);
    }

    [Fact]
    public void HashingOptions_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => IdentityPasswordHashingOptions.Configure(null!));
    }

    [Fact]
    public void LocalTiming_ReportsOnlyElapsedCost()
    {
        var account = Account();
        var hasher = CreateHasher();
        var password = BootstrapPassword.NormalizeAndValidateBase("synthetic timing passphrase");
        var warmup = hasher.HashPassword(account, password);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(account, warmup, password));
        const int samples = 3;
        var hashMilliseconds = 0.0;
        var verifyMilliseconds = 0.0;
        for (var sample = 0; sample < samples; sample++)
        {
            var timer = Stopwatch.StartNew();
            var hash = hasher.HashPassword(account, password);
            timer.Stop();
            hashMilliseconds += timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var result = hasher.VerifyHashedPassword(account, hash, password);
            timer.Stop();
            verifyMilliseconds += timer.Elapsed.TotalMilliseconds;
            Assert.Equal(PasswordVerificationResult.Success, result);
        }
        output.WriteLine("Local diagnostic only: {0} samples; hash mean {1:F1} ms; verify mean {2:F1} ms; iterations 220000.",
            samples, hashMilliseconds / samples, verifyMilliseconds / samples);
    }
}
