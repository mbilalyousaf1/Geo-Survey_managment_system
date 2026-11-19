using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.ControlPlane.Infrastructure.SignIn;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Sessions;
using GeoSurvey.Identity.Infrastructure.SignIn;
using GeoSurvey.Organizations.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class PlatformSessionStarterTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private const string Password = "synthetic session-start password";
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    // RFC 4226 steps 1, 2, and 3: enrollment then independent successful sign-ins.
    private const string EnrollmentCode = "287082";
    private const string Code = "359152";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(89);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private DbContextOptions<GlobalControlPlaneDbContext> ContextOptions(params IInterceptor[] interceptors)
    {
        using var context = database.CreateContext();
        return new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)context.GetService<IDbContextOptions>()).AddInterceptors(interceptors).Options;
    }

    private PlatformSessionStarter Starter(Clock clock, params IInterceptor[] interceptors) => new(ContextOptions(interceptors), _protector, clock);
    private PlatformPasswordEntryService PasswordEntry(Clock clock) => new(ContextOptions(), clock);
    private static MfaChallengeService Challenges(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);
    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private async Task<IdentityAccount> AccountAsync(string? email = null, bool organization = false)
    {
        await using var context = database.CreateContext();
        Organization? registry = null;
        if (organization)
        {
            registry = Organization.Create(OrganizationCode.Create("session-" + Guid.NewGuid().ToString("N")), "Synthetic Organization");
            context.Add(registry);
            await context.SaveChangesAsync();
        }
        email ??= Guid.NewGuid().ToString("N") + "@example.test";
        var account = registry is null ? IdentityAccount.CreatePlatformAccount(email) : IdentityAccount.CreateOrganizationAccount(registry.Id, email);
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        var hashing = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(hashing);
        account.PasswordHash = new PasswordHasher<IdentityAccount>(Options.Create(hashing)).HashPassword(account, Password);
        context.Add(account);
        await context.SaveChangesAsync();
        context.UserClaims.Add(new IdentityUserClaim<int> { UserId = account.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
        await context.SaveChangesAsync();
        return account;
    }

    private async Task<(IdentityAccount Account, MfaChallengeTicket Challenge)> SetupAsync(Clock clock)
    {
        var account = await AccountAsync();
        var enrollmentClock = new Clock { Now = DateTimeOffset.FromUnixTimeSeconds(59) };
        var enrollment = await PasswordEntry(enrollmentClock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Enrollment, enrollment.Purpose);
        await using var context = database.CreateContext();
        Assert.True(await new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, enrollmentClock), _protector, enrollmentClock)
            .TryStoreAsync(account.PublicId, enrollment.Challenge!.Token, Secret));
        Assert.True((await new MfaEnrollmentConfirmationService(context, _protector, enrollmentClock)
            .ConfirmAsync(account.PublicId, enrollment.Challenge.Token, EnrollmentCode)).Succeeded);
        var verification = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Verification, verification.Purpose);
        return (await context.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id), verification.Challenge!);
    }

    private async Task AssertNoSessionAsync(int accountId)
    {
        await using var context = database.CreateContext();
        Assert.False(await context.Set<TokenFamily>().AnyAsync(row => row.AccountId == accountId));
    }

    [Fact]
    public async Task ActualPasswordAndTotpFlow_CommitsBoundDigestOnlySession_AndReturnsOnce()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        var ciphertext = (await reader.Set<ConfirmedTotpCredential>().SingleAsync(row => row.AccountId == account.Id)).ProtectedSecret;
        await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AccessFailedCount, 2));
        Assert.False((await Starter(clock).StartAsync(challenge.Token, "wrong")).SessionCreated);

        var result = await Starter(clock).StartAsync(challenge.Token, Code);
        Assert.True(result.SessionCreated);
        var ticket = Assert.IsType<PlatformSessionTicket>(result.Ticket);
        Assert.Equal(account.PublicId, ticket.AccountPublicId);
        Assert.NotEqual(Guid.Empty, ticket.SessionPublicId);
        Assert.Equal(clock.Now, ticket.CreatedAtUtc);
        Assert.Equal(clock.Now.AddMinutes(30), ticket.RefreshExpiresAtUtc);
        Assert.Equal(clock.Now.AddHours(8), ticket.SessionExpiresAtUtc);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", ticket.RefreshToken);
        var bytes = Convert.FromBase64String(ticket.RefreshToken.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal(32, bytes.Length);
        Assert.Equal(ticket.RefreshToken, Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        Assert.NotEqual(challenge.Token, ticket.RefreshToken);
        Assert.DoesNotContain(ticket.RefreshToken, ticket.ToString()!);
        Assert.DoesNotContain(ticket.RefreshToken, result.ToString()!);

        var family = await reader.Set<TokenFamily>().SingleAsync(row => row.PublicId == ticket.SessionPublicId);
        Assert.Equal(account.Id, family.AccountId);
        Assert.Equal(account.SecurityStamp, family.SecurityStamp);
        Assert.Equal(ticket.CreatedAtUtc, family.CreatedAtUtc);
        Assert.Equal(ticket.SessionExpiresAtUtc, family.ExpiresAtUtc);
        Assert.Null(family.RevokedAtUtc);
        var storedToken = await reader.Set<RefreshToken>().SingleAsync(row => row.TokenFamilyId == family.Id);
        Assert.Equal(Digest(ticket.RefreshToken), storedToken.TokenHash);
        Assert.NotEqual(ticket.RefreshToken, storedToken.TokenHash);
        Assert.Equal(ticket.CreatedAtUtc, storedToken.CreatedAtUtc);
        Assert.Equal(ticket.RefreshExpiresAtUtc, storedToken.ExpiresAtUtc);
        Assert.Null(storedToken.ConsumedAtUtc);
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(2L, state.LastAcceptedStep);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.Null(state.FailureWindowStartedAtUtc);
        Assert.Null(state.BlockedUntilUtc);
        Assert.NotNull((await reader.Set<MfaChallenge>().SingleAsync(row => row.TokenHash == Digest(challenge.Token))).ConsumedAtUtc);
        var unchanged = await reader.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id);
        Assert.Equal(account.PasswordHash, unchanged.PasswordHash);
        Assert.Equal(account.SecurityStamp, unchanged.SecurityStamp);
        Assert.Equal(account.ConcurrencyStamp, unchanged.ConcurrencyStamp);
        Assert.Equal(2, unchanged.AccessFailedCount);
        Assert.True(unchanged.TwoFactorEnabled);
        Assert.Equal(ciphertext, (await reader.Set<ConfirmedTotpCredential>().SingleAsync(row => row.AccountId == account.Id)).ProtectedSecret);
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc == null));
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(challenge.Token, Code));
        Assert.Equal(1, await reader.Set<TokenFamily>().CountAsync(row => row.AccountId == account.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("359-152")]
    [InlineData("３５９１５２")]
    public async Task WrongOrMalformedCode_CommitsFailureWithoutCreatingSession(string? code)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(challenge.Token, code));
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Equal(1L, state.LastAcceptedStep);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, challenge.Token));
        await AssertNoSessionAsync(account.Id);
    }

    [Fact]
    public async Task FifthFailure_CommitsBlockAcrossReplacement_AndPostExpiryNeedsFreshPasswordProof()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        for (var index = 0; index < 5; index++)
        {
            if (index == 2)
                challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
            Assert.False((await Starter(clock).StartAsync(challenge.Token, "wrong")).SessionCreated);
        }
        var blockedUntil = clock.Now.AddMinutes(15);
        Assert.False((await Starter(clock).StartAsync(challenge.Token, Code)).SessionCreated);
        Assert.False((await PasswordEntry(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(5, state.FailedAttemptCount);
        Assert.Equal(blockedUntil, state.BlockedUntilUtc);
        await AssertNoSessionAsync(account.Id);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        Assert.False((await Starter(clock).StartAsync(challenge.Token, "081804")).SessionCreated);
        var fresh = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.True((await Starter(clock).StartAsync(fresh.Challenge!.Token, "081804")).SessionCreated);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("replaced")]
    [InlineData("stamp")]
    [InlineData("claim")]
    [InlineData("password-lockout")]
    [InlineData("legacy-password-lockout")]
    [InlineData("disabled")]
    [InlineData("consumed")]
    [InlineData("revoked")]
    [InlineData("mfa-block")]
    [InlineData("credential")]
    [InlineData("recovery")]
    public async Task IneligibleChallengeOrCurrentState_CreatesNoSessionAndDoesNotChargeCode(string change)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        switch (change)
        {
            case "expired": clock.Now = challenge.ExpiresAtUtc; break;
            case "replaced": await PasswordEntry(clock).BeginAsync(account.Email, Password); break;
            case "stamp": await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, "changed")); break;
            case "claim": await reader.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            case "password-lockout": await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LockoutEnd, clock.Now.AddMinutes(15))); break;
            case "legacy-password-lockout": await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LockoutEnabled, false).SetProperty(row => row.LockoutEnd, clock.Now.AddMinutes(15))); break;
            case "disabled": await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, false)); break;
            case "consumed": await Challenges(reader, clock).TryConsumeAsync(account.PublicId, MfaChallengePurpose.Verification, challenge.Token); break;
            case "revoked": await reader.Set<MfaChallenge>().Where(row => row.TokenHash == Digest(challenge.Token)).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, clock.Now)); break;
            case "mfa-block":
                await reader.Set<MfaVerificationState>().Where(row => row.AccountId == account.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.FailedAttemptCount, 5).SetProperty(row => row.FailureWindowStartedAtUtc, clock.Now)
                    .SetProperty(row => row.BlockedUntilUtc, clock.Now.AddMinutes(15)));
                break;
            case "credential": await reader.Set<ConfirmedTotpCredential>().Where(row => row.AccountId == account.Id).ExecuteDeleteAsync(); break;
            case "recovery": await reader.Set<MfaRecoveryCode>().Where(row => row.AccountId == account.Id).ExecuteDeleteAsync(); break;
        }
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(challenge.Token, Code));
        await AssertNoSessionAsync(account.Id);
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(change == "mfa-block" ? 5 : 0, state.FailedAttemptCount);
        Assert.Equal(1L, state.LastAcceptedStep);
    }

    [Fact]
    public async Task EnrollmentAndUnknownTokens_CannotCreateSessionOrSpendAnAccountsBudget()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var entry = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Enrollment, entry.Purpose);
        foreach (var token in new string?[] { entry.Challenge!.Token, null, "", "invalid", new('!', 43), new('a', 43) })
            Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(token, Code));
        await using var reader = database.CreateContext();
        Assert.False(await reader.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, entry.Challenge.Token));
        await AssertNoSessionAsync(account.Id);
    }

    [Fact]
    public async Task TokenResolvesOnlyItsStoredOwner_AndNewSignInDoesNotAdoptAnotherAccount()
    {
        var clock = new Clock();
        var (first, _) = await SetupAsync(clock);
        var (second, challenge) = await SetupAsync(clock);
        var result = await Starter(clock).StartAsync(challenge.Token, Code);
        Assert.Equal(second.PublicId, result.Ticket!.AccountPublicId);
        await AssertNoSessionAsync(first.Id);
        await using var reader = database.CreateContext();
        Assert.Equal(second.Id, (await reader.Set<TokenFamily>().SingleAsync(row => row.PublicId == result.Ticket.SessionPublicId)).AccountId);
        Assert.Equal(1L, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == first.Id)).LastAcceptedStep);
    }

    [Fact]
    public async Task MatchingOrganizationEmailAndSeededFactorCannotUsePlatformSessionStart()
    {
        var clock = new Clock();
        var (platform, _) = await SetupAsync(clock);
        var organization = await AccountAsync(platform.Email, organization: true);
        await using var reader = database.CreateContext();
        await reader.Users.Where(row => row.Id == organization.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, true));
        reader.Add(ConfirmedTotpCredential.Create(organization.Id, _protector.Protect(organization.PublicId, Secret), clock.Now));
        reader.Add(MfaRecoveryCode.Create(organization.Id, new string('A', 64), clock.Now));
        var raw = new string('Z', 43);
        reader.Add(MfaChallenge.Create(organization.Id, MfaChallengePurpose.Verification, Digest(raw), organization.SecurityStamp!, clock.Now, clock.Now.AddMinutes(10)));
        await reader.SaveChangesAsync();
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(raw, Code));
        await AssertNoSessionAsync(platform.Id);
        await AssertNoSessionAsync(organization.Id);
        Assert.False(await reader.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == organization.Id));
    }

    [Fact]
    public async Task FreshSignInCreatesIndependentFamily_WhileReusedTotpStepCannotCreateAnother()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        var first = (await Starter(clock).StartAsync(challenge.Token, Code)).Ticket!;
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(119);
        var entry = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        var second = (await Starter(clock).StartAsync(entry.Challenge!.Token, "969429")).Ticket!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.SessionPublicId, second.SessionPublicId);
        Assert.Equal(clock.Now.AddHours(8), second.SessionExpiresAtUtc);
        await using var reader = database.CreateContext();
        var families = await reader.Set<TokenFamily>().Where(row => row.AccountId == account.Id).ToListAsync();
        Assert.Equal(2, families.Count);
        Assert.All(families, family => Assert.Null(family.RevokedAtUtc));
        Assert.Equal(first.SessionExpiresAtUtc, families.Single(row => row.PublicId == first.SessionPublicId).ExpiresAtUtc);
        foreach (var family in families)
            Assert.Null((await reader.Set<RefreshToken>().SingleAsync(row => row.TokenFamilyId == family.Id)).ConsumedAtUtc);
        var replay = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock).StartAsync(replay.Challenge!.Token, "969429"));
        Assert.Equal(2, await reader.Set<TokenFamily>().CountAsync(row => row.AccountId == account.Id));
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(3L, state.LastAcceptedStep);
        Assert.Equal(1, state.FailedAttemptCount);
    }

    [Fact]
    public async Task SimultaneousCompletion_CommitsExactlyOneFamilyAndRefreshToken()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Starter(clock).StartAsync(challenge.Token, Code)));
        var success = Assert.Single(results, result => result.SessionCreated);
        Assert.All(results.Where(result => !result.SessionCreated), result => Assert.Null(result.Ticket));
        await using var reader = database.CreateContext();
        var family = await reader.Set<TokenFamily>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(success.Ticket!.SessionPublicId, family.PublicId);
        Assert.Single(await reader.Set<RefreshToken>().Where(row => row.TokenFamilyId == family.Id).ToListAsync());
    }

    private sealed class FailRefreshInsert : DbCommandInterceptor
    {
        public bool Attempted { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [identity].[RefreshTokens]", StringComparison.Ordinal))
            {
                Attempted = true;
                command.CommandText = "SELECT * FROM [identity].[SyntheticMissingRefreshInsertTarget]";
            }
            return ValueTask.FromResult(result);
        }
    }

    private async Task AssertMfaRestoredAsync(IdentityAccount account, MfaChallengeTicket challenge, Clock clock, int failures)
    {
        await AssertNoSessionAsync(account.Id);
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1L, state.LastAcceptedStep);
        Assert.Equal(failures, state.FailedAttemptCount);
        Assert.Null(state.BlockedUntilUtc);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, challenge.Token));
    }

    [Fact]
    public async Task RefreshInsertSqlFailure_RollsBackFamilyAndMfaAcceptance_WithoutLeakingDiagnostics()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        Assert.False((await Starter(clock).StartAsync(challenge.Token, "wrong")).SessionCreated);
        var fault = new FailRefreshInsert();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Starter(clock, fault).StartAsync(challenge.Token, Code));
        Assert.True(fault.Attempted);
        Assert.Equal("Platform session creation could not be completed.", error.Message);
        Assert.Null(error.InnerException);
        foreach (var value in new[] { challenge.Token, Secret, Password, account.SecurityStamp!, "SyntheticMissingRefreshInsertTarget" })
            Assert.DoesNotContain(value, error.ToString());
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 1);
        Assert.True((await Starter(clock).StartAsync(challenge.Token, Code)).SessionCreated);
    }

    private sealed class CommitProbe(int accountId, Func<DbTransaction, CancellationToken, Task>? beforeCommit = null) : DbTransactionInterceptor
    {
        public int FamilyCount { get; private set; }
        public int TokenCount { get; private set; }
        public long LastAcceptedStep { get; private set; }
        public bool WasCommitted { get; private set; }
        public IsolationLevel Isolation { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Isolation = transaction.IsolationLevel;
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            var account = command.CreateParameter();
            account.ParameterName = "@account";
            account.Value = accountId;
            command.Parameters.Add(account);
            command.CommandText = "SELECT COUNT(*) FROM [identity].[TokenFamilies] WHERE [AccountId] = @account";
            FamilyCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            command.CommandText = "SELECT COUNT(*) FROM [identity].[RefreshTokens] t INNER JOIN [identity].[TokenFamilies] f ON t.[TokenFamilyId] = f.[Id] WHERE f.[AccountId] = @account";
            TokenCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            command.CommandText = "SELECT [LastAcceptedStep] FROM [identity].[MfaVerificationStates] WHERE [AccountId] = @account";
            LastAcceptedStep = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (beforeCommit is not null)
                await beforeCommit(transaction, cancellationToken);
            return result;
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            WasCommitted = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ErrorAfterBothSessionWrites_RollsBackAllRowsNonceStepAndBudget()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        Assert.False((await Starter(clock).StartAsync(challenge.Token, "wrong")).SessionCreated);
        var probe = new CommitProbe(account.Id, async (transaction, cancellationToken) =>
        {
            await using var failure = transaction.Connection!.CreateCommand();
            failure.Transaction = transaction;
            failure.CommandText = "SELECT * FROM [identity].[SyntheticMissingSessionCommitTarget]";
            await failure.ExecuteNonQueryAsync(cancellationToken);
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Starter(clock, probe).StartAsync(challenge.Token, Code));
        Assert.Equal(1, probe.FamilyCount);
        Assert.Equal(1, probe.TokenCount);
        Assert.Equal(2L, probe.LastAcceptedStep);
        Assert.False(probe.WasCommitted);
        Assert.Equal("Platform session creation could not be completed.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("SyntheticMissingSessionCommitTarget", error.ToString());
        Assert.DoesNotContain(challenge.Token, error.ToString());
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 1);
        Assert.True((await Starter(clock).StartAsync(challenge.Token, Code)).SessionCreated);
    }

    [Fact]
    public async Task ResultCannotEscapeBeforeReadCommittedTransactionHasCommitted()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new CommitProbe(account.Id, async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var creation = Starter(clock, probe).StartAsync(challenge.Token, Code, deadline.Token);
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, probe.FamilyCount);
            Assert.Equal(1, probe.TokenCount);
            Assert.Equal(2L, probe.LastAcceptedStep);
            Assert.Equal(IsolationLevel.ReadCommitted, probe.Isolation);
            Assert.False(probe.WasCommitted);
            Assert.False(creation.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
        var result = await creation;
        Assert.True(probe.WasCommitted);
        Assert.True(result.SessionCreated);
        await using var reader = database.CreateContext();
        Assert.True(await reader.Set<TokenFamily>().AnyAsync(row => row.PublicId == result.Ticket!.SessionPublicId));
    }

    [Fact]
    public async Task CancellationAfterWrites_RollsBackWithoutReturningCredential()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        using var cancellation = new CancellationTokenSource();
        var probe = new CommitProbe(account.Id, (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Starter(clock, probe).StartAsync(challenge.Token, Code, cancellation.Token));
        Assert.Equal(1, probe.FamilyCount);
        Assert.Equal(1, probe.TokenCount);
        Assert.False(probe.WasCommitted);
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 0);
    }

    [Fact]
    public async Task PreCancelledCallAndAmbientTransaction_AreRejectedWithoutMutation()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Starter(clock).StartAsync(challenge.Token, Code, cancellation.Token));
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Starter(clock).StartAsync(challenge.Token, Code));
            Assert.Equal("Platform session creation must own its transaction.", error.Message);
        }
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableOrNoncanonicalSecret_FailsClosedWithoutChargingOrCreating(bool readable)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        var bad = readable ? _protector.Protect(account.PublicId, "noncanonical-secret") : "bad-ciphertext";
        await reader.Set<ConfirmedTotpCredential>().Where(row => row.AccountId == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProtectedSecret, bad));
        var error = await Assert.ThrowsAsync<CryptographicException>(() => Starter(clock).StartAsync(challenge.Token, Code));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(bad, error.ToString());
        Assert.DoesNotContain(challenge.Token, error.ToString());
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 0);
    }

    [Fact]
    public async Task PrivateContext_DoesNotFlushCallerTracker_AndDisablesInheritedSensitiveLogs()
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        await using var caller = database.CreateContext();
        var tracked = await caller.Users.SingleAsync(row => row.Id == account.Id);
        tracked.PhoneNumber = "synthetic-unsaved-phone";
        caller.ChangeTracker.DetectChanges();
        Assert.True(caller.ChangeTracker.HasChanges());
        var messages = new List<string>();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)caller.GetService<IDbContextOptions>())
            .EnableSensitiveDataLogging().LogTo(messages.Add).Options;
        var result = await new PlatformSessionStarter(options, _protector, clock).StartAsync(challenge.Token, Code);
        Assert.True(result.SessionCreated);
        Assert.Empty(messages);
        Assert.True(caller.ChangeTracker.HasChanges());
        Assert.Equal("synthetic-unsaved-phone", tracked.PhoneNumber);
        await using var reader = database.CreateContext();
        Assert.Null((await reader.Users.SingleAsync(row => row.Id == account.Id)).PhoneNumber);
    }

    private sealed class AfterAcceptance(Func<DbCommand, CancellationToken, Task> action) : DbCommandInterceptor
    {
        private int _invoked;
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("[identity].[MfaVerificationStates]", StringComparison.Ordinal)
                && command.CommandText.Contains("[LastAcceptedStep]", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _invoked, 1) == 0)
                await action(command, cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("claim")]
    public async Task ChangedStateAfterMfaAcceptance_IsNotAdopted_AndAcceptanceRollsBack(string change)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        Assert.False((await Starter(clock).StartAsync(challenge.Token, "wrong")).SessionCreated);
        var mutation = new AfterAcceptance(async (command, cancellationToken) =>
        {
            await using var update = command.Connection!.CreateCommand();
            update.Transaction = command.Transaction;
            update.CommandText = change == "stamp"
                ? "UPDATE [identity].[Accounts] SET [SecurityStamp] = N'synthetic-changed-stamp' WHERE [Id] = @account"
                : "DELETE FROM [identity].[AccountClaims] WHERE [UserId] = @account";
            var parameter = update.CreateParameter();
            parameter.ParameterName = "@account";
            parameter.Value = account.Id;
            update.Parameters.Add(parameter);
            await update.ExecuteNonQueryAsync(cancellationToken);
        });
        Assert.Same(PlatformSessionStartResult.Rejected, await Starter(clock, mutation).StartAsync(challenge.Token, Code));
        await AssertMfaRestoredAsync(account, challenge, clock, failures: 1);
        await using var reader = database.CreateContext();
        Assert.Equal(account.SecurityStamp, (await reader.Users.SingleAsync(row => row.Id == account.Id)).SecurityStamp);
        Assert.True(await reader.UserClaims.AnyAsync(row => row.UserId == account.Id && row.ClaimType == PlatformAdministratorClaims.Type && row.ClaimValue == PlatformAdministratorClaims.Administrator));
        Assert.True((await Starter(clock).StartAsync(challenge.Token, Code)).SessionCreated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IssuanceClockIsReadAfterMfaAcceptance_AndNonUtcRollsBack(bool invalidOffset)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        var later = DateTimeOffset.FromUnixTimeSeconds(90);
        var advance = new AfterAcceptance((_, _) =>
        {
            clock.Now = invalidOffset ? later.ToOffset(TimeSpan.FromHours(5)) : later;
            return Task.CompletedTask;
        });
        if (invalidOffset)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Starter(clock, advance).StartAsync(challenge.Token, Code));
            Assert.Equal("The platform session clock must supply UTC timestamps.", error.Message);
            clock.Now = later;
            await AssertMfaRestoredAsync(account, challenge, clock, failures: 0);
        }
        else
        {
            var result = await Starter(clock, advance).StartAsync(challenge.Token, Code);
            Assert.Equal(later, result.Ticket!.CreatedAtUtc);
            Assert.Equal(later.AddMinutes(30), result.Ticket.RefreshExpiresAtUtc);
            Assert.Equal(later.AddHours(8), result.Ticket.SessionExpiresAtUtc);
        }
    }

    private sealed class AccountLockProbe : DbCommandInterceptor
    {
        private int _attempted;
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[identity].[Accounts] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _attempted, 1) == 0)
                Attempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockWaitUsesFreshTime_AndExpiredChallengeCreatesNoSession(bool expireChallenge)
    {
        var clock = new Clock();
        var (account, challenge) = await SetupAsync(clock);
        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {account.PublicId}");
        var probe = new AccountLockProbe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var creation = Starter(clock, probe).StartAsync(challenge.Token, Code, deadline.Token);
        try
        {
            await probe.Attempted.Task.WaitAsync(deadline.Token);
            clock.Now = expireChallenge ? challenge.ExpiresAtUtc : DateTimeOffset.FromUnixTimeSeconds(90);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var result = await creation;
        if (expireChallenge)
        {
            Assert.Same(PlatformSessionStartResult.Rejected, result);
            await AssertNoSessionAsync(account.Id);
            await using var reader = database.CreateContext();
            Assert.Equal(0, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
        }
        else
        {
            Assert.Equal(clock.Now, result.Ticket!.CreatedAtUtc);
            Assert.Equal(clock.Now.AddMinutes(30), result.Ticket.RefreshExpiresAtUtc);
            Assert.Equal(clock.Now.AddHours(8), result.Ticket.SessionExpiresAtUtc);
        }
    }
}
