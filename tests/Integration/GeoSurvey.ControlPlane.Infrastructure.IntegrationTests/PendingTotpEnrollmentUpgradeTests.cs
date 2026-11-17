using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class PendingTotpEnrollmentUpgradeTests(MfaChallengeMigrationDatabase database) : IClassFixture<MfaChallengeMigrationDatabase>
{
    [Fact]
    public async Task UpgradeFromChallengeSchema_PreservesAccountsChallengesAndBootstrapState()
    {
        await using var context = database.CreateContext();
        Assert.Equal(3, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var account = IdentityAccount.CreatePlatformAccount("pending-upgrade@example.test");
        account.SecurityStamp = "synthetic-upgrade-stamp";
        context.Add(account);
        await context.SaveChangesAsync();
        var created = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        var challenge = MfaChallenge.Create(account.Id, MfaChallengePurpose.Enrollment, new string('A', 64), account.SecurityStamp, created, created.AddMinutes(10));
        context.Add(challenge);
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(5, (await reader.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await reader.Set<PendingTotpEnrollment>().ToListAsync());
        Assert.Empty(await reader.BootstrapCompletions.ToListAsync());
        var loaded = await reader.Set<MfaChallenge>().SingleAsync();
        Assert.Equal(challenge.TokenHash, loaded.TokenHash);
        Assert.Equal(account.Id, loaded.AccountId);
        Assert.False((await reader.Users.SingleAsync()).TwoFactorEnabled);
        Assert.Equal(account.PublicId, (await reader.Users.SingleAsync()).PublicId);
        Assert.Empty(await reader.Database.GetPendingMigrationsAsync());
    }
}
