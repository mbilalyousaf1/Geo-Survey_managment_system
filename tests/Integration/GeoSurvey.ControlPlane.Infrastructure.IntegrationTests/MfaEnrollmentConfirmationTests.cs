using System.Data.Common;
using System.Security.Cryptography;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Organizations.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MfaEnrollmentConfirmationTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    // Published RFC 6238 SHA1 secret/code at Unix time 1111111109, reduced to the approved six digits.
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string Code = "081804";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        public override DateTimeOffset GetUtcNow() => Now;
        public long Step => Now.ToUnixTimeSeconds() / 30;
    }

    private MfaEnrollmentConfirmationService Service(GlobalControlPlaneDbContext context, Clock clock) => new(context, _protector, clock);
    private static MfaChallengeService Challenges(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);
    private PendingTotpEnrollmentService Pending(GlobalControlPlaneDbContext context, Clock clock) => new(new EfPendingTotpEnrollmentStore(context, clock), _protector, clock);

    private async Task<(IdentityAccount Account, MfaChallengeTicket Ticket)> SetupAsync(Clock clock)
    {
        var account = IdentityAccount.CreatePlatformAccount(Guid.NewGuid().ToString("N") + "@example.test");
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        // Synthetic persistence sentinel, not a real credential or password-verification substitute.
        account.PasswordHash = "synthetic-password-hash-sentinel";
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
        Assert.True(await Pending(context, clock).TryStoreAsync(account.PublicId, ticket.Token, Secret));
        return (account, ticket);
    }

    private async Task AssertNotConfirmedAsync(int accountId, bool challengeConsumed = false)
    {
        await using var reader = database.CreateContext();
        Assert.False((await reader.Users.AsNoTracking().SingleAsync(row => row.Id == accountId)).TwoFactorEnabled);
        Assert.False(await reader.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == accountId));
        Assert.False(await reader.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == accountId));
        Assert.Equal(challengeConsumed, await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == accountId && row.ConsumedAtUtc != null));
    }

    [Fact]
    public async Task RealTotpConfirmation_CommitsAllState_ReturnsOnlyInitialCodes_AndPreservesAccountData()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var pending = await context.Set<PendingTotpEnrollment>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        // Also prove completion revokes other outstanding purposes and rotates their stamp binding.
        var other = MfaChallenge.Create(account.Id, MfaChallengePurpose.Verification, new string('F', 64), account.SecurityStamp!, clock.Now, ticket.ExpiresAtUtc);
        context.Add(other);
        await context.SaveChangesAsync();
        var result = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        Assert.True(result.Succeeded);
        Assert.Equal(10, result.RecoveryCodes.Count);
        Assert.Equal(10, result.RecoveryCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.RecoveryCodes, code => Assert.DoesNotContain(code, result.ToString()!));

        await using var reader = database.CreateContext();
        var confirmed = await reader.Set<ConfirmedTotpCredential>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(pending.ProtectedSecret, confirmed.ProtectedSecret);
        Assert.DoesNotContain(Secret, confirmed.ProtectedSecret);
        Assert.Equal(Secret, _protector.Unprotect(account.PublicId, confirmed.ProtectedSecret));
        Assert.Equal(clock.Now, confirmed.ConfirmedAtUtc);
        Assert.False(await reader.Set<PendingTotpEnrollment>().AnyAsync(row => row.AccountId == account.Id));
        var codes = await reader.Set<MfaRecoveryCode>().Where(row => row.AccountId == account.Id).ToListAsync();
        Assert.Equal(result.RecoveryCodes.Select(code => MfaRecoveryCodes.ComputeIssuedCodeDigest(account.PublicId, code)).Order(), codes.Select(row => row.CodeHash).Order());
        Assert.All(codes, row =>
        {
            Assert.Equal(64, row.CodeHash.Length);
            Assert.DoesNotContain(row.CodeHash, result.RecoveryCodes);
            Assert.Null(row.ConsumedAtUtc);
            Assert.Equal(clock.Now, row.CreatedAtUtc);
        });
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.True(loaded.TwoFactorEnabled);
        Assert.NotEqual(account.SecurityStamp, loaded.SecurityStamp);
        Assert.NotEqual(account.ConcurrencyStamp, loaded.ConcurrencyStamp);
        Assert.Equal(account.PasswordHash, loaded.PasswordHash);
        Assert.Equal(account.PublicId, loaded.PublicId);
        Assert.Equal(account.Email, loaded.Email);
        Assert.Equal(account.AccountScope, loaded.AccountScope);
        Assert.Null(loaded.OrganizationId);
        Assert.Contains(await reader.UserClaims.Where(row => row.UserId == account.Id).ToListAsync(), claim =>
            claim.ClaimType == PlatformAdministratorClaims.Type && claim.ClaimValue == PlatformAdministratorClaims.Administrator);
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(clock.Step, state.LastAcceptedStep);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.NotNull((await reader.Set<MfaChallenge>().SingleAsync(row => row.Id == pending.ChallengeId)).ConsumedAtUtc);
        Assert.NotNull((await reader.Set<MfaChallenge>().SingleAsync(row => row.Id == other.Id)).RevokedAtUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Verification));
        var fresh = await Challenges(reader, clock).IssueAsync(account.PublicId, loaded.SecurityStamp!, MfaChallengePurpose.Verification);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, fresh.Token));

        var repeat = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        Assert.False(repeat.Succeeded);
        Assert.Empty(repeat.RecoveryCodes);
        Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, fresh.Token, Code)).Succeeded);
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("81804")]
    [InlineData("081-804")]
    [InlineData("０８１８０４")]
    public async Task InvalidCode_CommitsFailureBudget_WithoutPartialConfirmation(string? code)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var result = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, code);
        Assert.False(result.Succeeded);
        Assert.Empty(result.RecoveryCodes);
        await AssertNotConfirmedAsync(account.Id);
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Null(state.LastAcceptedStep);
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
    }

    [Fact]
    public async Task FifthWrongCode_BlocksConfirmation_EvenWithTheCorrectCode()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        for (var index = 0; index < 5; index++)
            Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, "wrong")).Succeeded);
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(5, state.FailedAttemptCount);
        Assert.Equal(clock.Now.AddMinutes(15), state.BlockedUntilUtc);
        var result = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        Assert.False(result.Succeeded);
        Assert.Empty(result.RecoveryCodes);
        await AssertNotConfirmedAsync(account.Id);
        Assert.NotNull((await context.Set<MfaChallenge>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).RevokedAtUtc);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("replaced")]
    [InlineData("stamp")]
    [InlineData("claim")]
    [InlineData("locked")]
    [InlineData("enabled")]
    [InlineData("consumed")]
    public async Task InvalidChallenge_CannotConfirmOrChargeTheAccount(string change)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        switch (change)
        {
            case "expired": clock.Now = ticket.ExpiresAtUtc; break;
            case "replaced": await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment); break;
            case "stamp": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, "changed")); break;
            case "claim": await context.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            case "locked": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LockoutEnabled, true).SetProperty(row => row.LockoutEnd, clock.Now.AddMinutes(5))); break;
            case "enabled": await context.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, true)); break;
            case "consumed": await Challenges(context, clock).TryConsumeAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token); break;
        }
        var result = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        Assert.False(result.Succeeded);
        Assert.Empty(result.RecoveryCodes);
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False(await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False(await context.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task WrongAccountOrMalformedToken_CannotConfirm()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var (other, _) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        Assert.False((await Service(context, clock).ConfirmAsync(other.PublicId, ticket.Token, Code)).Succeeded);
        Assert.False((await Service(context, clock).ConfirmAsync(Guid.Empty, ticket.Token, Code)).Succeeded);
        foreach (var token in new string?[] { null, "", "malformed", new('!', 43), new('a', 43) })
            Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, token, Code)).Succeeded);
        await AssertNotConfirmedAsync(account.Id);
        await AssertNotConfirmedAsync(other.Id);
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id || row.AccountId == other.Id));
    }

    [Fact]
    public async Task MissingPendingSecret_CannotConsumeChallengeOrConfirm()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await context.Set<PendingTotpEnrollment>().Where(row => row.AccountId == account.Id).ExecuteDeleteAsync();
        Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code)).Succeeded);
        await AssertNotConfirmedAsync(account.Id);
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingConfirmedOrRecoveryData_IsNotOverwritten(bool recoveryData)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var original = _protector.Protect(account.PublicId, Secret);
        if (recoveryData)
            context.Add(MfaRecoveryCode.Create(account.Id, new string('A', 64), clock.Now));
        else
            context.Add(ConfirmedTotpCredential.Create(account.Id, original, clock.Now));
        await context.SaveChangesAsync();
        var result = await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        Assert.False(result.Succeeded);
        Assert.Empty(result.RecoveryCodes);
        Assert.False((await context.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).TwoFactorEnabled);
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
        if (recoveryData)
            Assert.Equal(new string('A', 64), (await context.Set<MfaRecoveryCode>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).CodeHash);
        else
            Assert.Equal(original, (await context.Set<ConfirmedTotpCredential>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).ProtectedSecret);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableOrInvalidProtectedSecret_FailsClosedWithoutChargingCodeFailure(bool readableButInvalid)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        var corrupt = readableButInvalid ? _protector.Protect(account.PublicId, "not-a-canonical-secret") : "invalid-ciphertext";
        await context.Set<PendingTotpEnrollment>().Where(row => row.AccountId == account.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProtectedSecret, corrupt));
        var error = await Assert.ThrowsAsync<CryptographicException>(() => Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code));
        Assert.DoesNotContain(corrupt, error.ToString());
        Assert.Null(error.InnerException);
        await AssertNotConfirmedAsync(account.Id);
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task PreviouslyAcceptedTotpStep_CannotConfirmANewEnrollmentChallenge()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await new MfaVerificationService(new EfMfaVerificationStore(context, clock))
                .TryAcceptStepAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token, clock.Step));
            await transaction.CommitAsync();
        }
        var fresh = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.True(await Pending(context, clock).TryStoreAsync(account.PublicId, fresh.Token, Secret));
        Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, fresh.Token, Code)).Succeeded);
        var state = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Equal(clock.Step, state.LastAcceptedStep);
        Assert.False(await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id));
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Token));
    }

    [Fact]
    public async Task ConcurrentConfirmation_ReturnsOneCodeBatch_AndStoresOneCredential()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code);
        }));
        Assert.Single(results, result => result.Succeeded);
        Assert.All(results.Where(result => !result.Succeeded), result => Assert.Empty(result.RecoveryCodes));
        await using var reader = database.CreateContext();
        Assert.Equal(1, await reader.Set<ConfirmedTotpCredential>().CountAsync(row => row.AccountId == account.Id));
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task OrganizationScope_CannotConfirmEvenWithClaimSecretAndSeededChallenge()
    {
        var clock = new Clock();
        await using var context = database.CreateContext();
        var organization = Organization.Create(OrganizationCode.Create("mfa-" + Guid.NewGuid().ToString("N")), "Synthetic Organization");
        context.Add(organization);
        await context.SaveChangesAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, "scope@example.test");
        account.SecurityStamp = "synthetic-stamp";
        context.Add(account);
        await context.SaveChangesAsync();
        context.UserClaims.Add(new IdentityUserClaim<int> { UserId = account.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
        var token = new string('Z', 43);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));
        var challenge = MfaChallenge.Create(account.Id, MfaChallengePurpose.Enrollment, hash, account.SecurityStamp, clock.Now, clock.Now.AddMinutes(10));
        context.Add(challenge);
        await context.SaveChangesAsync();
        context.Add(PendingTotpEnrollment.Create(account.Id, challenge.Id, _protector.Protect(account.PublicId, Secret), clock.Now));
        await context.SaveChangesAsync();
        Assert.False((await Service(context, clock).ConfirmAsync(account.PublicId, token, Code)).Succeeded);
        await AssertNotConfirmedAsync(account.Id);
    }

    private sealed class FailAfterWrites : DbCommandInterceptor
    {
        public int RecoveryInserts { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO [identity].[MfaRecoveryCodes]", StringComparison.Ordinal))
                RecoveryInserts++;
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal) && command.CommandText.Contains("[identity].[PendingTotpEnrollments]", StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic failure after partial confirmation writes.");
            return ValueTask.FromResult(result);
        }
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

    [Fact]
    public async Task ExpiryWhileWaitingForAccountLock_RejectsWithoutChargingOrConfirming()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {account.PublicId}");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var probe = new AccountLockProbe();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)holder.GetService<IDbContextOptions>()).AddInterceptors(probe).Options;
        await using var writer = new GlobalControlPlaneDbContext(options);
        var confirmation = Service(writer, clock).ConfirmAsync(account.PublicId, ticket.Token, Code, cancellation.Token);
        await probe.Attempted.Task.WaitAsync(cancellation.Token);
        clock.Now = ticket.ExpiresAtUtc;
        await transaction.RollbackAsync();
        Assert.False((await confirmation).Succeeded);
        await AssertNotConfirmedAsync(account.Id);
        Assert.False(await writer.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task StorageConstraints_EnforceDigestUniquenessAccountForeignKeysAndTimestampOrder()
    {
        var clock = new Clock();
        await using var context = database.CreateContext();
        var account = IdentityAccount.CreatePlatformAccount("schema-" + Guid.NewGuid().ToString("N") + "@example.test");
        context.Add(account);
        await context.SaveChangesAsync();
        var digest = MfaRecoveryCodes.ComputeIssuedCodeDigest(account.PublicId, MfaRecoveryCodes.GenerateBatch()[0]);
        context.Add(ConfirmedTotpCredential.Create(account.Id, _protector.Protect(account.PublicId, Secret), clock.Now));
        context.Add(MfaRecoveryCode.Create(account.Id, digest, clock.Now));
        await context.SaveChangesAsync();
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [identity].[MfaRecoveryCodes] ([AccountId], [CodeHash], [CreatedAtUtc], [ConsumedAtUtc]) VALUES ({account.Id}, {digest}, {clock.Now}, NULL)"));
        Assert.Equal(2627, duplicate.Number);
        var missing = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [identity].[TotpCredentials] ([AccountId], [ProtectedSecret], [ConfirmedAtUtc]) VALUES ({int.MaxValue}, N'synthetic-ciphertext', {clock.Now})"));
        Assert.Equal(547, missing.Number);
        var invalidTime = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [identity].[MfaRecoveryCodes] SET [ConsumedAtUtc] = {clock.Now.AddTicks(-1)} WHERE [AccountId] = {account.Id}"));
        Assert.Equal(547, invalidTime.Number);
        // This account has no claims/challenges/state; new credential/code FKs block deletion themselves.
        var deletion = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [identity].[Accounts] WHERE [Id] = {account.Id}"));
        Assert.Equal(547, deletion.Number);
    }

    [Fact]
    public async Task FailureAfterCredentialCodesAndStampWrites_RollsBackEverything_AndAllowsRetry()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var normal = database.CreateContext();
        var interceptor = new FailAfterWrites();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)normal.GetService<IDbContextOptions>()).AddInterceptors(interceptor).Options;
        await using (var failing = new GlobalControlPlaneDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(failing, clock).ConfirmAsync(account.PublicId, ticket.Token, Code));
        Assert.Equal(10, interceptor.RecoveryInserts);
        await AssertNotConfirmedAsync(account.Id);
        var restored = await normal.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.Equal(account.SecurityStamp, restored.SecurityStamp);
        Assert.Equal(account.ConcurrencyStamp, restored.ConcurrencyStamp);
        Assert.True(await normal.Set<PendingTotpEnrollment>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False(await normal.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
        Assert.True(await Challenges(normal, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token));
        Assert.True((await Service(normal, clock).ConfirmAsync(account.PublicId, ticket.Token, Code)).Succeeded);
    }

    [Fact]
    public async Task CancellationAndAmbientTransaction_DoNotProduceConfirmation()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code, cancellation.Token));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context, clock).ConfirmAsync(account.PublicId, ticket.Token, Code));
            await transaction.RollbackAsync();
        }
        await AssertNotConfirmedAsync(account.Id);
    }
}
