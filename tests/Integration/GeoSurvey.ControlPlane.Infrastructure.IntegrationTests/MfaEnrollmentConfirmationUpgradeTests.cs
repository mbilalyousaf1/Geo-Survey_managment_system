using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MfaEnrollmentConfirmationUpgradeTests(MfaVerificationMigrationDatabase database) : IClassFixture<MfaVerificationMigrationDatabase>
{
    [Fact]
    public async Task Upgrade_PreservesPendingAndControlState_WithoutConfirmingOrIssuingCodes()
    {
        await using var context = database.CreateContext();
        Assert.Equal(5, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var account = IdentityAccount.CreatePlatformAccount("confirmation-upgrade@example.test");
        account.SecurityStamp = "synthetic-stamp";
        context.Add(account);
        await context.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var challenge = MfaChallenge.Create(account.Id, MfaChallengePurpose.Enrollment, new string('C', 64), account.SecurityStamp, now, now.AddMinutes(10));
        context.Add(challenge);
        await context.SaveChangesAsync();
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var ciphertext = protector.Protect(account.PublicId, "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        context.Add(PendingTotpEnrollment.Create(account.Id, challenge.Id, ciphertext, now));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [identity].[MfaVerificationStates] ([AccountId], [FailedAttemptCount], [FailureWindowStartedAtUtc], [BlockedUntilUtc], [LastAcceptedStep]) VALUES ({account.Id}, 2, {now}, NULL, 123)");

        await context.Database.MigrateAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(8, (await reader.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await reader.Set<ConfirmedTotpCredential>().ToListAsync());
        Assert.Empty(await reader.Set<MfaRecoveryCode>().ToListAsync());
        Assert.Equal(ciphertext, (await reader.Set<PendingTotpEnrollment>().SingleAsync()).ProtectedSecret);
        var state = await reader.Set<MfaVerificationState>().SingleAsync();
        Assert.Equal(2, state.FailedAttemptCount);
        Assert.Equal(123L, state.LastAcceptedStep);
        Assert.False((await reader.Users.SingleAsync()).TwoFactorEnabled);
        Assert.Null((await reader.Set<MfaChallenge>().SingleAsync()).ConsumedAtUtc);
        Assert.Empty(await reader.BootstrapCompletions.ToListAsync());
        Assert.Empty(await reader.Database.GetPendingMigrationsAsync());
    }
}
