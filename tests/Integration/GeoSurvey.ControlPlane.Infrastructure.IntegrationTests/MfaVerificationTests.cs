using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MfaVerificationTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public long Step => Now.ToUnixTimeSeconds() / 30;
    }

    private static MfaVerificationService Service(GlobalControlPlaneDbContext context, Clock clock) =>
        new(new EfMfaVerificationStore(context, clock));
    private static MfaChallengeService Challenges(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);

    private async Task<(IdentityAccount Account, MfaChallengeTicket Ticket)> SetupAsync(Clock clock)
    {
        var account = IdentityAccount.CreatePlatformAccount(Guid.NewGuid().ToString("N") + "@example.test");
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        await using var context = database.CreateContext();
        context.Add(account);
        await context.SaveChangesAsync();
        context.UserClaims.Add(new IdentityUserClaim<int>
        {
            UserId = account.Id,
            ClaimType = PlatformAdministratorClaims.Type,
            ClaimValue = PlatformAdministratorClaims.Administrator
        });
        await context.SaveChangesAsync();
        var ticket = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        return (account, ticket);
    }

    private async Task<bool> AcceptAsync(IdentityAccount account, string token, long step, Clock clock)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var accepted = await Service(context, clock).TryAcceptStepAsync(account.PublicId, MfaChallengePurpose.Enrollment, token, step);
        // Commit rejected valid-challenge attempts too, so failed-attempt increments are durable.
        await transaction.CommitAsync();
        return accepted;
    }

    [Fact]
    public async Task FifthFailure_BlocksRevokesChallengesAndDoesNotExtendBlock()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var pending = new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, clock), protector, clock);
        Assert.True(await pending.TryStoreAsync(account.PublicId, ticket.Token, "synthetic-secret"));
        for (var index = 1; index <= 5; index++)
        {
            Assert.Equal(index == 5 ? MfaFailureResult.Blocked : MfaFailureResult.FailureRecorded,
                await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
            var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
            Assert.Equal(index, state.FailedAttemptCount);
        }
        var blockedUntil = clock.Now.AddMinutes(15);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        var blocked = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(blockedUntil, blocked.BlockedUntilUtc);
        Assert.Equal(5, blocked.FailedAttemptCount);
        Assert.Null(await pending.ReadSecretAsync(account.PublicId, ticket.Token));
        Assert.False(await pending.TryStoreAsync(account.PublicId, ticket.Token, "not-stored"));
        Assert.False(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment));
        Assert.All(await context.Set<MfaChallenge>().AsNoTracking().Where(row => row.AccountId == account.Id).ToListAsync(), row => Assert.NotNull(row.RevokedAtUtc));

        clock.Now = blockedUntil.AddTicks(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment));
        clock.Now = blockedUntil;
        Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.Equal(MfaFailureResult.FailureRecorded, await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Token));
        var reset = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, reset.FailedAttemptCount);
        Assert.Null(reset.BlockedUntilUtc);
        Assert.Equal(clock.Now, reset.FailureWindowStartedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureWindow_ResetsExactlyAtFifteenMinutes(bool boundary)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        Assert.Equal(MfaFailureResult.FailureRecorded, await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        clock.Now = clock.Now.AddMinutes(15).AddTicks(boundary ? 0 : -1);
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Token);
        Assert.Equal(boundary ? 1 : 2, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("stamp")]
    [InlineData("purpose")]
    public async Task ReplacementStampOrPurposeChange_DoesNotResetBudget(string change)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using (var first = database.CreateContext())
            for (var index = 0; index < 4; index++)
                await Service(first, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        // A new context/store represents process-local state being discarded; all controls remain SQL-backed.
        await using var context = database.CreateContext();
        var stamp = account.SecurityStamp!;
        var purpose = MfaChallengePurpose.Enrollment;
        if (change == "stamp")
        {
            stamp = Guid.NewGuid().ToString("N");
            await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, stamp));
        }
        if (change == "purpose")
        {
            purpose = MfaChallengePurpose.Verification;
            await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, true));
        }
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, stamp, purpose);
        Assert.Equal(MfaFailureResult.Blocked, await Service(context, clock).RecordFailureAsync(account.PublicId, purpose, fresh.Token));
        Assert.Equal(5, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("replaced")]
    [InlineData("stamp")]
    [InlineData("claim")]
    [InlineData("locked")]
    public async Task InvalidChallenge_DoesNotChangeCounter(string change)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        switch (change)
        {
            case "expired": clock.Now = ticket.ExpiresAtUtc; break;
            case "consumed": await Challenges(context, clock).TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token); break;
            case "replaced": await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment); break;
            case "stamp": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, "changed")); break;
            case "claim": await context.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            case "locked":
                await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LockoutEnabled, true).SetProperty(row => row.LockoutEnd, clock.Now.AddHours(1))); break;
        }
        Assert.Equal(MfaFailureResult.InvalidChallenge, await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.Equal(1, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Fact]
    public async Task ConcurrentFailures_CountExactlyFiveAndRevokeBeforeFurtherUpdates()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        }));
        Assert.Equal(4, results.Count(result => result == MfaFailureResult.FailureRecorded));
        Assert.Single(results, result => result == MfaFailureResult.Blocked);
        Assert.Equal(7, results.Count(result => result == MfaFailureResult.InvalidChallenge));
        await using var reader = database.CreateContext();
        Assert.Equal(5, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Fact]
    public async Task AcceptedStep_ConsumesChallengeResetsFailuresAndPreservesNoAuthenticationGrant()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using (var context = database.CreateContext())
            await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        Assert.True(await AcceptAsync(account, ticket.Token, clock.Step, clock));
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(clock.Step, state.LastAcceptedStep);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.Null(state.FailureWindowStartedAtUtc);
        Assert.Null(state.BlockedUntilUtc);
        Assert.False(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.False((await reader.Users.SingleAsync(row => row.Id == account.Id)).TwoFactorEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(2)]
    public async Task ReusedOlderOrOutOfWindowSteps_AreRejectedAndCountAsFailures(int offset)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        Assert.True(await AcceptAsync(account, ticket.Token, clock.Step, clock));
        await using var context = database.CreateContext();
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.False(await AcceptAsync(account, fresh.Token, clock.Step + offset, clock));
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(clock.Step, state.LastAcceptedStep);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Token));
    }

    [Fact]
    public async Task ConcurrentAcceptance_ExactlyOneWins()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AcceptAsync(account, ticket.Token, clock.Step, clock)));
        Assert.Single(results, accepted => accepted);
    }

    [Fact]
    public async Task Rollback_RestoresFailuresReplayStateAndChallenge()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await Service(context, clock).TryAcceptStepAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token, clock.Step));
            await transaction.RollbackAsync();
        }
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Null(state.LastAcceptedStep);
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            for (var index = 0; index < 4; index++)
                await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
            await transaction.RollbackAsync();
        }
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.Equal(1, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Fact]
    public async Task WrongAccountTokenOrPurpose_DoesNotCreateControlState()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var (other, _) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(other.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
        foreach (var token in new string?[] { null, "", "malformed", new('!', 43), new('a', 43) })
            Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, token));
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id || row.AccountId == other.Id));
    }

    [Fact]
    public async Task BlockExpiry_DoesNotErasePreviouslyAcceptedStep()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var accepted = clock.Step;
        Assert.True(await AcceptAsync(account, ticket.Token, accepted, clock));
        await using var context = database.CreateContext();
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        for (var index = 0; index < 5; index++)
            await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Token);
        clock.Now = clock.Now.AddMinutes(15);
        var next = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        await Service(context, clock).RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, next.Token);
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(accepted, state.LastAcceptedStep);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Null(state.BlockedUntilUtc);
        Assert.True(await AcceptAsync(account, next.Token, clock.Step, clock));
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(0, -1)]
    public async Task SqlConstraints_RejectInvalidCounterOrNegativeStep(int count, long step)
    {
        var clock = new Clock();
        var (account, _) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        DateTimeOffset? window = count > 0 ? clock.Now : null;
        var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [identity].[MfaVerificationStates] ([AccountId], [FailedAttemptCount], [FailureWindowStartedAtUtc], [BlockedUntilUtc], [LastAcceptedStep]) VALUES ({account.Id}, {count}, {window}, NULL, {step})"));
        Assert.Equal(547, error.Number);
    }

    [Fact]
    public async Task AcceptanceWithoutCallerTransaction_IsRejected()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context, clock).TryAcceptStepAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token, clock.Step));
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }
}
