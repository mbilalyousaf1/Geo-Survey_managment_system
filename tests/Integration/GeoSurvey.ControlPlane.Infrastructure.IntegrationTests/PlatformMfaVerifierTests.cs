using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.ControlPlane.Infrastructure.SignIn;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.SignIn;
using GeoSurvey.Organizations.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class PlatformMfaVerifierTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private const string Password = "synthetic MFA verification password";
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    // RFC 4226 secret/step vectors: enrollment at step 1, verification at step 2.
    private const string EnrollmentCode = "287082";
    private const string Code = "359152";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(89);
        public override DateTimeOffset GetUtcNow() => Now;
        public long Step => Now.ToUnixTimeSeconds() / 30;
    }

    private DbContextOptions<GlobalControlPlaneDbContext> ContextOptions(params IInterceptor[] interceptors)
    {
        using var context = database.CreateContext();
        return new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)context.GetService<IDbContextOptions>()).AddInterceptors(interceptors).Options;
    }

    private PlatformMfaVerifier Verifier(GlobalControlPlaneDbContext context, Clock clock) => new(context, _protector, clock);
    private PlatformPasswordEntryService PasswordEntry(Clock clock) => new(ContextOptions(), clock);
    private static MfaChallengeService Challenges(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);
    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private async Task<IdentityAccount> AccountAsync(bool organization = false)
    {
        await using var context = database.CreateContext();
        Organization? registry = null;
        if (organization)
        {
            registry = Organization.Create(OrganizationCode.Create("verify-" + Guid.NewGuid().ToString("N")), "Synthetic Organization");
            context.Add(registry);
            await context.SaveChangesAsync();
        }
        var email = Guid.NewGuid().ToString("N") + "@example.test";
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

    private async Task<(IdentityAccount Account, MfaChallengeTicket Ticket)> SetupAsync(Clock clock)
    {
        var account = await AccountAsync();
        var enrollmentClock = new Clock { Now = DateTimeOffset.FromUnixTimeSeconds(59) };
        var entry = await PasswordEntry(enrollmentClock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Enrollment, entry.Purpose);
        await using var context = database.CreateContext();
        Assert.True(await new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, enrollmentClock), _protector, enrollmentClock)
            .TryStoreAsync(account.PublicId, entry.Challenge!.Token, Secret));
        Assert.True((await new MfaEnrollmentConfirmationService(context, _protector, enrollmentClock)
            .ConfirmAsync(account.PublicId, entry.Challenge.Token, EnrollmentCode)).Succeeded);
        var second = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Verification, second.Purpose);
        return (await context.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id), second.Challenge!);
    }

    private async Task<PlatformMfaVerificationResult> AttemptAsync(string? token, string? code, Clock clock)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var result = await Verifier(context, clock).VerifyAsync(token, code);
        // Denied code/replay outcomes must commit their failure increments too.
        await transaction.CommitAsync();
        return result;
    }

    [Fact]
    public async Task PasswordToConfirmedFactorFlow_ConsumesOnceAndReturnsOnlyTheStoredPublicSubject()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AccessFailedCount, 2));
        Assert.Same(PlatformMfaVerificationResult.Rejected, await AttemptAsync(ticket.Token, "wrong", clock));
        var result = await AttemptAsync(ticket.Token, Code, clock);
        Assert.True(result.IsVerified);
        Assert.Equal(account.PublicId, result.AccountPublicId);
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(2L, state.LastAcceptedStep);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.Null(state.BlockedUntilUtc);
        Assert.Null(state.FailureWindowStartedAtUtc);
        var hash = Digest(ticket.Token);
        Assert.NotNull((await reader.Set<MfaChallenge>().AsNoTracking().SingleAsync(row => row.TokenHash == hash)).ConsumedAtUtc);
        Assert.Same(PlatformMfaVerificationResult.Rejected, await AttemptAsync(ticket.Token, Code, clock));
        var unchanged = await reader.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id);
        Assert.Equal(2, unchanged.AccessFailedCount);
        Assert.Equal(account.SecurityStamp, unchanged.SecurityStamp);
        Assert.Equal(account.ConcurrencyStamp, unchanged.ConcurrencyStamp);
        Assert.Equal(account.PasswordHash, unchanged.PasswordHash);
        Assert.True(unchanged.TwoFactorEnabled);
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc == null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("000000")]
    [InlineData("359-152")]
    [InlineData("３５９１５２")]
    public async Task WrongOrMalformedCode_RecordsFailureWithoutConsumingChallenge(string? code)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        Assert.Same(PlatformMfaVerificationResult.Rejected, await AttemptAsync(ticket.Token, code, clock));
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.Equal(1L, state.LastAcceptedStep); // Enrollment, not a successful sign-in.
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
    }

    [Fact]
    public async Task FifthFailure_BlocksCorrectCode_AndRequiresAFreshPasswordChallengeAfterExpiry()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        for (var index = 0; index < 5; index++)
            Assert.False((await AttemptAsync(ticket.Token, "wrong", clock)).IsVerified);
        Assert.False((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(5, state.FailedAttemptCount);
        Assert.Equal(clock.Now.AddMinutes(15), state.BlockedUntilUtc);
        Assert.False((await PasswordEntry(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        // Another published RFC timestamp/code, well after the block. Earlier accepted step remains protected.
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        Assert.False((await AttemptAsync(ticket.Token, "081804", clock)).IsVerified);
        var fresh = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.True((await AttemptAsync(fresh.Challenge!.Token, "081804", clock)).IsVerified);
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
    public async Task InvalidChallengeOrCurrentAccountState_CannotVerifyOrChangeBudget(string change)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        switch (change)
        {
            case "expired": clock.Now = ticket.ExpiresAtUtc; break;
            case "replaced": await PasswordEntry(clock).BeginAsync(account.Email, Password); break;
            case "stamp": await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.SecurityStamp, "changed")); break;
            case "claim": await reader.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            case "password-lockout": await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LockoutEnd, (DateTimeOffset?)clock.Now.AddMinutes(15))); break;
            case "legacy-password-lockout": await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LockoutEnabled, false).SetProperty(user => user.LockoutEnd, (DateTimeOffset?)clock.Now.AddMinutes(15))); break;
            case "disabled": await reader.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.TwoFactorEnabled, false)); break;
            case "consumed": await Challenges(reader, clock).TryConsumeAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token); break;
        }
        Assert.False((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.Equal(1L, state.LastAcceptedStep);
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("recovery")]
    public async Task MissingConfirmedState_IsNotAdoptedOrCharged(string missing)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        if (missing == "credential")
            await reader.Set<ConfirmedTotpCredential>().Where(row => row.AccountId == account.Id).ExecuteDeleteAsync();
        else
            await reader.Set<MfaRecoveryCode>().Where(row => row.AccountId == account.Id).ExecuteDeleteAsync();
        Assert.False((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
        Assert.Equal(0, (await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
    }

    [Fact]
    public async Task ConsumedRecoveryBackups_DoNotDisableConfirmedAuthenticator()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        await reader.Set<MfaRecoveryCode>().Where(row => row.AccountId == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, (DateTimeOffset?)clock.Now));
        Assert.True((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc != null));
    }

    [Fact]
    public async Task EnrollmentAndUnknownTokens_CannotSelectAnAccountOrSpendVerificationBudget()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var entry = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.Equal(MfaChallengePurpose.Enrollment, entry.Purpose);
        foreach (var token in new string?[] { entry.Challenge!.Token, null, "", "invalid", new('!', 43), new('a', 43) })
            Assert.Same(PlatformMfaVerificationResult.Rejected, await AttemptAsync(token, Code, clock));
        await using var reader = database.CreateContext();
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, entry.Challenge.Token));
        Assert.False(await reader.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task VerifiedSubject_IsResolvedOnlyFromTheStoredTokenOwner()
    {
        var clock = new Clock();
        var (first, _) = await SetupAsync(clock);
        var (second, ticket) = await SetupAsync(clock);
        var result = await AttemptAsync(ticket.Token, Code, clock);
        Assert.Equal(second.PublicId, result.AccountPublicId);
        Assert.NotEqual(first.PublicId, result.AccountPublicId);
        await using var reader = database.CreateContext();
        Assert.Equal(1L, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == first.Id)).LastAcceptedStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableOrNoncanonicalSecret_FailsClosedWithoutCodeFailureCharge(bool readable)
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        var bad = readable ? _protector.Protect(account.PublicId, "noncanonical-secret") : "bad-ciphertext";
        await reader.Set<ConfirmedTotpCredential>().Where(row => row.AccountId == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProtectedSecret, bad));
        var error = await Assert.ThrowsAsync<CryptographicException>(() => AttemptAsync(ticket.Token, Code, clock));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(bad, error.ToString());
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
        Assert.Equal(0, (await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }

    [Fact]
    public async Task CiphertextCopiedFromAnotherAccount_CannotVerify()
    {
        var clock = new Clock();
        var (first, _) = await SetupAsync(clock);
        var (second, ticket) = await SetupAsync(clock);
        await using var reader = database.CreateContext();
        var wrong = (await reader.Set<ConfirmedTotpCredential>().SingleAsync(row => row.AccountId == first.Id)).ProtectedSecret;
        await reader.Set<ConfirmedTotpCredential>().Where(row => row.AccountId == second.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProtectedSecret, wrong));
        await Assert.ThrowsAsync<CryptographicException>(() => AttemptAsync(ticket.Token, Code, clock));
        Assert.Equal(1L, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == second.Id)).LastAcceptedStep);
    }

    [Fact]
    public async Task AcceptedStep_CannotBeReplayedWithANewPasswordVerifiedChallenge()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        Assert.True((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
        var next = await PasswordEntry(clock).BeginAsync(account.Email, Password);
        Assert.False((await AttemptAsync(next.Challenge!.Token, Code, clock)).IsVerified);
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(2L, state.LastAcceptedStep);
        Assert.Equal(1, state.FailedAttemptCount);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, next.Challenge.Token));
    }

    [Fact]
    public async Task ConcurrentVerification_ExactlyOneConsumptionSucceeds()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AttemptAsync(ticket.Token, Code, clock)));
        Assert.Single(results, result => result.IsVerified);
        Assert.All(results.Where(result => result.IsVerified), result => Assert.Equal(account.PublicId, result.AccountPublicId));
    }

    [Fact]
    public async Task CallerRollback_RestoresNonceReplayAndFailureBudget()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        Assert.False((await AttemptAsync(ticket.Token, "wrong", clock)).IsVerified);
        await using var context = database.CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            var provisional = await Verifier(context, clock).VerifyAsync(ticket.Token, Code);
            Assert.True(provisional.IsVerified);
            // A real SQL error stands in for downstream session persistence failure; no session implementation is faked.
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => context.Database.ExecuteSqlRawAsync("SELECT * FROM [identity].[SyntheticMissingSessionWriteTarget];"));
            await transaction.RollbackAsync();
            // The provisional result is intentionally discarded after rollback.
        }
        var restored = await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(1, restored.FailedAttemptCount);
        Assert.Equal(1L, restored.LastAcceptedStep);
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.False((await Verifier(context, clock).VerifyAsync(ticket.Token, "wrong")).IsVerified);
            await transaction.RollbackAsync();
        }
        Assert.Equal(1, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
        Assert.True((await AttemptAsync(ticket.Token, Code, clock)).IsVerified);
    }

    [Fact]
    public async Task NoOrUnsupportedTransaction_CannotVerify_AndUnsafeLoggingIsRejected()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Verifier(context, clock).VerifyAsync(ticket.Token, Code));
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Verifier(context, clock).VerifyAsync(ticket.Token, Code));
            await transaction.RollbackAsync();
        }
        var unsafeOptions = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(ContextOptions()).EnableSensitiveDataLogging().Options;
        await using var unsafeContext = new GlobalControlPlaneDbContext(unsafeOptions);
        Assert.Throws<InvalidOperationException>(() => Verifier(unsafeContext, clock));
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
    }

    [Fact]
    public async Task PreCancelledVerification_CannotConsumeChallenge()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Verifier(context, clock).VerifyAsync(ticket.Token, Code, cancellation.Token));
        await transaction.RollbackAsync();
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
    }

    [Fact]
    public async Task OrganizationScope_CannotUseSeededVerificationChallengeEvenWithMatchingClaimAndFactor()
    {
        var clock = new Clock();
        var account = await AccountAsync(organization: true);
        await using var context = database.CreateContext();
        await context.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.TwoFactorEnabled, true));
        context.Add(ConfirmedTotpCredential.Create(account.Id, _protector.Protect(account.PublicId, Secret), clock.Now));
        context.Add(MfaRecoveryCode.Create(account.Id, MfaRecoveryCodes.ComputeIssuedCodeDigest(account.PublicId, MfaRecoveryCodes.GenerateBatch()[0]), clock.Now));
        var token = new string('Z', 43);
        context.Add(MfaChallenge.Create(account.Id, MfaChallengePurpose.Verification, Digest(token), account.SecurityStamp!, clock.Now, clock.Now.AddMinutes(10)));
        await context.SaveChangesAsync();
        Assert.Same(PlatformMfaVerificationResult.Rejected, await AttemptAsync(token, Code, clock));
        Assert.False(await context.Set<MfaVerificationState>().AnyAsync(row => row.AccountId == account.Id));
    }

    private sealed class AccountLockProbe(Func<Task>? beforeLock = null) : DbCommandInterceptor
    {
        private int _attempted;
        public TaskCompletionSource Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[identity].[Accounts] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _attempted, 1) == 0)
            {
                if (beforeLock is not null)
                    await beforeLock();
                Attempted.TrySetResult();
            }
            return result;
        }
    }

    [Fact]
    public async Task ExpiryWhileWaitingForAccountLock_IsRecheckedWithoutChargingFailure()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var holder = database.CreateContext();
        await using var heldTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await holder.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [SecurityStamp] FROM [identity].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [PublicId] = {account.PublicId}");
        var probe = new AccountLockProbe();
        await using var caller = new GlobalControlPlaneDbContext(ContextOptions(probe));
        await using var completion = await caller.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var verification = Verifier(caller, clock).VerifyAsync(ticket.Token, Code, cancellation.Token);
        await probe.Attempted.Task.WaitAsync(cancellation.Token);
        clock.Now = ticket.ExpiresAtUtc;
        await heldTransaction.RollbackAsync();
        Assert.False((await verification).IsVerified);
        await completion.CommitAsync();
        var state = await caller.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.Equal(1L, state.LastAcceptedStep);
    }

    [Fact]
    public async Task ChangedStoredTokenOwnerAfterRouting_IsRevalidatedUnderTheAccountLock()
    {
        var clock = new Clock();
        var (first, ticket) = await SetupAsync(clock);
        var (second, _) = await SetupAsync(clock);
        var hash = Digest(ticket.Token);
        var change = new AccountLockProbe(async () =>
        {
            await using var writer = database.CreateContext();
            await using var mutation = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await writer.Set<MfaChallenge>().Where(row => row.AccountId == second.Id && row.Purpose == MfaChallengePurpose.Verification && row.RevokedAtUtc == null && row.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)clock.Now));
            // Test-only corruption/race; production exposes no operation to transfer challenge ownership.
            await writer.Set<MfaChallenge>().Where(row => row.TokenHash == hash).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AccountId, second.Id));
            await mutation.CommitAsync();
        });
        await using var caller = new GlobalControlPlaneDbContext(ContextOptions(change));
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        Assert.False((await Verifier(caller, clock).VerifyAsync(ticket.Token, Code)).IsVerified);
        await transaction.CommitAsync();
        foreach (var account in new[] { first, second })
        {
            var state = await caller.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
            Assert.Equal(1L, state.LastAcceptedStep);
            Assert.Equal(0, state.FailedAttemptCount);
        }
    }

    private sealed class FailControlWrite : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("[identity].[MfaVerificationStates]", StringComparison.Ordinal))
                command.CommandText = "SELECT * FROM [identity].[SyntheticMissingMfaControlWriteTarget];";
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task SqlControlFailure_IsSanitized_AndCallerRollbackPreservesTheNonce()
    {
        var clock = new Clock();
        var (account, ticket) = await SetupAsync(clock);
        await using var context = new GlobalControlPlaneDbContext(ContextOptions(new FailControlWrite()));
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Verifier(context, clock).VerifyAsync(ticket.Token, "000000"));
            Assert.Equal("Platform MFA verification could not be completed.", error.Message);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(ticket.Token, error.ToString());
            Assert.DoesNotContain(Secret, error.ToString());
            Assert.DoesNotContain("SyntheticMissingMfaControlWriteTarget", error.ToString());
            await transaction.RollbackAsync();
        }
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, ticket.Token));
        Assert.Equal(0, (await context.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).FailedAttemptCount);
    }
}
