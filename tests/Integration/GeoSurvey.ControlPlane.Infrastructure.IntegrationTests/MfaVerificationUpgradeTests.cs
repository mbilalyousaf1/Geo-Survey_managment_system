using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MfaVerificationUpgradeTests(PendingEnrollmentMigrationDatabase database) : IClassFixture<PendingEnrollmentMigrationDatabase>
{
    [Fact]
    public async Task Upgrade_PreservesPendingCiphertextAndDoesNotSeedControlState()
    {
        await using var context = database.CreateContext();
        Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var account = IdentityAccount.CreatePlatformAccount("verification-upgrade@example.test");
        account.SecurityStamp = "synthetic-stamp";
        context.Add(account);
        await context.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var challenge = MfaChallenge.Create(account.Id, MfaChallengePurpose.Enrollment, new string('B', 64), account.SecurityStamp, now, now.AddMinutes(10));
        context.Add(challenge);
        await context.SaveChangesAsync();
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var ciphertext = protector.Protect(account.PublicId, "synthetic-secret");
        context.Add(PendingTotpEnrollment.Create(account.Id, challenge.Id, ciphertext, now));
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(8, (await reader.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await reader.Set<MfaVerificationState>().ToListAsync());
        Assert.Equal(ciphertext, (await reader.Set<PendingTotpEnrollment>().SingleAsync()).ProtectedSecret);
        Assert.Equal("synthetic-secret", protector.Unprotect(account.PublicId, (await reader.Set<PendingTotpEnrollment>().SingleAsync()).ProtectedSecret));
        Assert.False((await reader.Users.SingleAsync()).TwoFactorEnabled);
        Assert.Null((await reader.Set<MfaChallenge>().SingleAsync()).ConsumedAtUtc);
        Assert.Empty(await reader.BootstrapCompletions.ToListAsync());
    }
}
