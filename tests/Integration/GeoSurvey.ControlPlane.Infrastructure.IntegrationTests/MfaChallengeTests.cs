using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MfaChallengeTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private async Task<IdentityAccount> AccountAsync(bool enabled = false, bool administrator = true)
    {
        var account = IdentityAccount.CreatePlatformAccount(Guid.NewGuid().ToString("N") + "@example.test");
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.TwoFactorEnabled = enabled;
        await using var context = database.CreateContext();
        context.Add(account);
        await context.SaveChangesAsync();
        if (administrator)
        {
            context.UserClaims.Add(new IdentityUserClaim<int>
            {
                UserId = account.Id,
                ClaimType = PlatformAdministratorClaims.Type,
                ClaimValue = PlatformAdministratorClaims.Administrator
            });
            await context.SaveChangesAsync();
        }
        return account;
    }

    private static MfaChallengeService Service(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);

    [Theory]
    [InlineData(false, MfaChallengePurpose.Enrollment)]
    [InlineData(true, MfaChallengePurpose.Verification)]
    public async Task Ticket_IsOpaqueAndStoredOnlyAsHash(bool enabled, MfaChallengePurpose purpose)
    {
        var account = await AccountAsync(enabled);
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var ticket = await service.IssueAsync(account.PublicId, account.SecurityStamp!, purpose);
        Assert.Equal(43, ticket.Token.Length);
        Assert.DoesNotContain(ticket.Token, ticket.ToString()!);
        Assert.Equal(clock.Now.AddMinutes(10), ticket.ExpiresAtUtc);
        var stored = await context.Set<MfaChallenge>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.NotEqual(ticket.Token, stored.TokenHash);
        Assert.True(await service.IsValidAsync(account.PublicId, purpose, ticket.Token));
        Assert.False(await service.IsValidAsync(Guid.NewGuid(), purpose, ticket.Token));
        var otherPurpose = purpose == MfaChallengePurpose.Enrollment ? MfaChallengePurpose.Verification : MfaChallengePurpose.Enrollment;
        Assert.False(await service.IsValidAsync(account.PublicId, otherPurpose, ticket.Token));
        Assert.False(await service.IsValidAsync(account.PublicId, purpose, "malformed"));
        clock.Now = ticket.ExpiresAtUtc;
        Assert.False(await service.IsValidAsync(account.PublicId, purpose, ticket.Token));
        Assert.False(await service.TryConsumeAsync(account.PublicId, purpose, ticket.Token));
    }

    [Fact]
    public async Task Replacement_RevokesPreviousAndConcurrentConsumptionSucceedsOnce()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var previous = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        var current = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.False(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var consumer = database.CreateContext();
            return await Service(consumer, clock).TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, current.Token);
        }));
        Assert.Single(results, result => result);
        Assert.False(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, current.Token));
        Assert.Equal(2, await context.Set<MfaChallenge>().CountAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task ConcurrentIssuance_LeavesOneUsableChallenge()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        var tickets = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await Service(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        }));
        await using var reader = database.CreateContext();
        var service = Service(reader, clock);
        var valid = new List<bool>();
        foreach (var ticket in tickets)
            valid.Add(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.Single(valid, item => item);
    }

    [Fact]
    public async Task StampChange_InvalidatesOutstandingChallenge()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var ticket = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, "changed-stamp"));
        Assert.False(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.False(await service.TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MissingClaimOrWrongMfaState_RefusesIssuance(bool administrator, bool enabled)
    {
        var account = await AccountAsync(enabled, administrator);
        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context, new Clock()).IssueAsync(
            account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment));
    }

    [Fact]
    public async Task LockoutAndClaimRemoval_InvalidateChallenge()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var ticket = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.LockoutEnabled, true).SetProperty(row => row.LockoutEnd, clock.Now.AddHours(1)));
        Assert.False(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment));
        await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LockoutEnd, (DateTimeOffset?)null));
        Assert.True(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        await context.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync();
        Assert.False(await service.TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
    }

    [Fact]
    public async Task WrongStamp_DoesNotRevokeExistingTicket()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var ticket = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(account.PublicId, "wrong-stamp", MfaChallengePurpose.Enrollment));
        Assert.True(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
    }

    [Fact]
    public async Task Consumption_CanRollBackWithCallerTransaction()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var ticket = await service.IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await service.TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
            await transaction.RollbackAsync();
        }
        Assert.True(await service.IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
    }
}
