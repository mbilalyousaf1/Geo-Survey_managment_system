using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class PendingTotpEnrollmentTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private const string Secret = "synthetic-enrollment-secret";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AccountLockProbe : DbCommandInterceptor
    {
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[identity].[Accounts] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
                Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private PendingTotpEnrollmentService Service(GlobalControlPlaneDbContext context, Clock clock) =>
        new(new EfPendingTotpEnrollmentStore(context, clock), _protector, clock);

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
        var ticket = await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
            .IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        return (account, ticket);
    }

    [Fact]
    public async Task PendingSecret_IsEncryptedBoundToChallenge_AndDoesNotEnableMfa()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.Null(await service.ReadSecretAsync(account.PublicId, ticket.Token));
        Assert.True(await service.TryStoreAsync(account.PublicId, ticket.Token, Secret));
        var row = await context.Set<PendingTotpEnrollment>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.DoesNotContain(Secret, row.ProtectedSecret);
        Assert.DoesNotContain(ticket.Token, row.ProtectedSecret);
        Assert.Equal(clock.Now, row.CreatedAtUtc);
        Assert.Equal(Secret, _protector.Unprotect(account.PublicId, row.ProtectedSecret));
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(Guid.NewGuid(), row.ProtectedSecret));
        Assert.Equal(Secret, await service.ReadSecretAsync(account.PublicId, ticket.Token));
        Assert.False((await context.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).TwoFactorEnabled);
        Assert.True(await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
            .IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
    }

    [Fact]
    public async Task SameChallenge_CannotOverwritePendingSecret()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.True(await service.TryStoreAsync(account.PublicId, ticket.Token, Secret));
        Assert.False(await service.TryStoreAsync(account.PublicId, ticket.Token, "replacement"));
        Assert.Equal(Secret, await service.ReadSecretAsync(account.PublicId, ticket.Token));
    }

    [Fact]
    public async Task ReplacementChallenge_CannotAdoptPreviousSecret_ButCanStoreNewOne()
    {
        var clock = new Clock();
        var (account, first) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.True(await service.TryStoreAsync(account.PublicId, first.Token, Secret));
        var second = await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
            .IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.Null(await service.ReadSecretAsync(account.PublicId, first.Token));
        Assert.Null(await service.ReadSecretAsync(account.PublicId, second.Token));
        Assert.False(await service.TryStoreAsync(account.PublicId, first.Token, "rejected"));
        Assert.True(await service.TryStoreAsync(account.PublicId, second.Token, "fresh-secret"));
        Assert.Equal("fresh-secret", await service.ReadSecretAsync(account.PublicId, second.Token));
        Assert.Equal(1, await context.Set<PendingTotpEnrollment>().CountAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task ConcurrentSameChallenge_ExactlyOneSecretIsStored()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
        {
            await using var context = database.CreateContext();
            var secret = "synthetic-" + index;
            return (Stored: await Service(context, clock).TryStoreAsync(account.PublicId, ticket.Token, secret), Secret: secret);
        }));
        var winner = Assert.Single(results, result => result.Stored);
        await using var reader = database.CreateContext();
        Assert.Equal(winner.Secret, await Service(reader, clock).ReadSecretAsync(account.PublicId, ticket.Token));
    }

    [Fact]
    public async Task ExpiryWhileWaitingForAccountLock_RefusesWrite()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {account.Id}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var probe = new AccountLockProbe();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)holder.GetService<IDbContextOptions>()).AddInterceptors(probe).Options;
        await using var writer = new GlobalControlPlaneDbContext(options);
        var pending = Service(writer, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret, cancellation.Token);
        await probe.Attempted.Task.WaitAsync(cancellation.Token);
        clock.Now = ticket.ExpiresAtUtc;
        await transaction.RollbackAsync();
        Assert.False(await pending);
        Assert.False(await writer.Set<PendingTotpEnrollment>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task ConcurrentConsumption_DoesNotLeaveAReadableEnrollmentSecret()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var writer = database.CreateContext();
        await using var consumer = database.CreateContext();
        var write = Service(writer, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret);
        var consume = new MfaChallengeService(new EfMfaChallengeStore(consumer), clock)
            .TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        await Task.WhenAll(write, consume);
        Assert.True(await consume);
        Assert.Null(await Service(writer, clock).ReadSecretAsync(account.PublicId, ticket.Token));
    }

    [Fact]
    public async Task CallerRollback_RemovesPendingWrite_WithoutConsumingChallenge()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await Service(context, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret));
            await transaction.RollbackAsync();
        }
        Assert.Null(await Service(context, clock).ReadSecretAsync(account.PublicId, ticket.Token));
        Assert.True(await Service(context, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("stamp")]
    [InlineData("claim")]
    [InlineData("locked")]
    [InlineData("enabled")]
    public async Task IneligibleChallenge_CannotReadOrChangePendingSecret(string change)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.True(await service.TryStoreAsync(account.PublicId, ticket.Token, Secret));
        switch (change)
        {
            case "expired": clock.Now = ticket.ExpiresAtUtc; break;
            case "consumed":
                Assert.True(await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
                    .TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
                break;
            case "stamp": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, "changed")); break;
            case "claim": await context.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            case "locked":
                await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LockoutEnabled, true).SetProperty(row => row.LockoutEnd, clock.Now.AddHours(1))); break;
            case "enabled": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, true)); break;
        }
        Assert.Null(await service.ReadSecretAsync(account.PublicId, ticket.Token));
        Assert.False(await service.TryStoreAsync(account.PublicId, ticket.Token, "not-stored"));
        var persisted = await context.Set<PendingTotpEnrollment>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(Secret, _protector.Unprotect(account.PublicId, persisted.ProtectedSecret));
    }

    [Fact]
    public async Task WrongAccountOrMalformedToken_CannotStoreOrRead()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var service = Service(context, clock);
        Assert.False(await service.TryStoreAsync(Guid.NewGuid(), ticket.Token, Secret));
        Assert.Null(await service.ReadSecretAsync(Guid.NewGuid(), ticket.Token));
        foreach (var token in new string?[] { null, "", "malformed", new('!', 43) })
        {
            Assert.False(await service.TryStoreAsync(account.PublicId, token, Secret));
            Assert.Null(await service.ReadSecretAsync(account.PublicId, token));
        }
        Assert.False(await service.TryStoreAsync(Guid.Empty, ticket.Token, Secret));
        Assert.Equal(0, await context.Set<PendingTotpEnrollment>().CountAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task VerificationChallenge_CannotBeUsedForPendingEnrollment()
    {
        var clock = new Clock();
        var (account, _) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, true));
        var ticket = await new MfaChallengeService(new EfMfaChallengeStore(context), clock)
            .IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Verification);
        Assert.False(await Service(context, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret));
        Assert.Null(await Service(context, clock).ReadSecretAsync(account.PublicId, ticket.Token));
    }

    [Fact]
    public async Task CompositeForeignKey_RejectsAnotherAccountsChallenge()
    {
        var clock = new Clock();
        var (first, _) = await SetupAsync(clock);
        var (second, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(ticket.Token)));
        var challenge = await context.Set<MfaChallenge>().SingleAsync(row => row.TokenHash == hash);
        context.Add(PendingTotpEnrollment.Create(first.Id, challenge.Id, _protector.Protect(first.PublicId, Secret), clock.Now));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.NotEqual(first.Id, second.Id);
    }
}
