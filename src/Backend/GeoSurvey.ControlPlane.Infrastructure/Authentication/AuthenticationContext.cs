using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.Authentication;

internal static class AuthenticationContext
{
    internal static void RequireOwnedTransaction()
    {
        if (System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Authentication operations must own their transaction.");
    }

    internal static GlobalControlPlaneDbContext Create(DbContextOptions<GlobalControlPlaneDbContext> options) => new(
        new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(options).UseLoggerFactory(NullLoggerFactory.Instance)
            .LogTo(_ => { }, LogLevel.None).EnableSensitiveDataLogging(false).Options);

    internal static DateTimeOffset UtcNow(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The authentication clock must supply UTC timestamps.");
        return now;
    }

    internal static PasswordHasher<IdentityAccount> CreateHasher()
    {
        var options = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(options);
        return new PasswordHasher<IdentityAccount>(Options.Create(options));
    }

    private static readonly Lazy<(IdentityAccount Account, string Hash)> Dummy = new(() =>
    {
        var user = IdentityAccount.CreatePlatformAccount("dummy-auth@example.invalid");
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return (user, CreateHasher().HashPassword(user, Convert.ToHexString(bytes))); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    });

    internal static void DummyPassword(string password) =>
        _ = CreateHasher().VerifyHashedPassword(Dummy.Value.Account, Dummy.Value.Hash, password);
}
