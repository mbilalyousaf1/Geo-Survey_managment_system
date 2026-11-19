using System.Data.Common;
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

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class PlatformPasswordEntryTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private const string Password = "synthetic entry password 42";
    private const string TotpSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());

    private sealed class Clock : TimeProvider
    {
        // Published RFC secret/code pair used only when preparing a genuinely confirmed factor.
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private DbContextOptions<GlobalControlPlaneDbContext> Options(params IInterceptor[] interceptors)
    {
        using var context = database.CreateContext();
        return new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)context.GetService<IDbContextOptions>()).AddInterceptors(interceptors).Options;
    }

    private PlatformPasswordEntryService Service(Clock clock, params IInterceptor[] interceptors) => new(Options(interceptors), clock);
    private static MfaChallengeService Challenges(GlobalControlPlaneDbContext context, Clock clock) => new(new EfMfaChallengeStore(context), clock);
    private static PasswordHasher<IdentityAccount> Hasher(int iterations = IdentityPasswordHashingOptions.IterationCount,
        PasswordHasherCompatibilityMode compatibility = PasswordHasherCompatibilityMode.IdentityV3) =>
        new(Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions { CompatibilityMode = compatibility, IterationCount = iterations }));

    private async Task<IdentityAccount> AccountAsync(string? email = null, string password = Password, bool administrator = true,
        bool organization = false, int iterations = IdentityPasswordHashingOptions.IterationCount,
        PasswordHasherCompatibilityMode compatibility = PasswordHasherCompatibilityMode.IdentityV3)
    {
        await using var context = database.CreateContext();
        Organization? registry = null;
        if (organization)
        {
            registry = Organization.Create(OrganizationCode.Create("entry-" + Guid.NewGuid().ToString("N")), "Synthetic Organization");
            context.Add(registry);
            await context.SaveChangesAsync();
        }
        var address = email ?? Guid.NewGuid().ToString("N") + "@example.test";
        var account = registry is null ? IdentityAccount.CreatePlatformAccount(address) : IdentityAccount.CreateOrganizationAccount(registry.Id, address);
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        account.PasswordHash = Hasher(iterations, compatibility).HashPassword(account, PlatformSignInPassword.NormalizeForVerification(password));
        context.Add(account);
        await context.SaveChangesAsync();
        if (administrator)
        {
            context.UserClaims.Add(new IdentityUserClaim<int> { UserId = account.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
            await context.SaveChangesAsync();
        }
        return account;
    }

    private async Task<IdentityAccount> EnrollAsync(IdentityAccount account, Clock clock)
    {
        await using var context = database.CreateContext();
        var ticket = await Challenges(context, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        Assert.True(await new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, clock), _protector, clock)
            .TryStoreAsync(account.PublicId, ticket.Token, TotpSecret));
        Assert.True((await new MfaEnrollmentConfirmationService(context, _protector, clock).ConfirmAsync(account.PublicId, ticket.Token, "081804")).Succeeded);
        return await context.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
    }

    [Fact]
    public async Task CorrectPassword_ReturnsOnlyRestrictedEnrollmentChallenge_WithScopedNormalizedEmail()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var result = await Service(clock).BeginAsync("  " + account.Email!.ToUpperInvariant() + "  ", Password);
        Assert.True(result.ChallengeIssued);
        Assert.Equal(MfaChallengePurpose.Enrollment, result.Purpose);
        Assert.Equal(clock.Now.AddMinutes(10), result.Challenge!.ExpiresAtUtc);
        Assert.DoesNotContain(result.Challenge.Token, result.ToString()!);
        Assert.DoesNotContain(Password, result.ToString()!);
        await using var reader = database.CreateContext();
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, result.Challenge.Token));
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.True(loaded.LockoutEnabled); // Bootstrap/factory defaults false; the password path establishes policy.
        Assert.False(loaded.TwoFactorEnabled);
        Assert.Equal(account.PasswordHash, loaded.PasswordHash);
        Assert.Equal(account.SecurityStamp, loaded.SecurityStamp);
        Assert.Equal(account.Email, loaded.Email);
        Assert.Equal(0, loaded.AccessFailedCount);
        Assert.False(await reader.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False(await reader.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task CorrectPasswordForConfirmedFactor_IssuesVerification_EvenWhenBackupsAreConsumed()
    {
        var clock = new Clock();
        var account = await EnrollAsync(await AccountAsync(), clock);
        await using var context = database.CreateContext();
        await context.Set<MfaRecoveryCode>().Where(row => row.AccountId == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, (DateTimeOffset?)clock.Now));
        var result = await Service(clock).BeginAsync(account.Email, Password);
        Assert.True(result.ChallengeIssued);
        Assert.Equal(MfaChallengePurpose.Verification, result.Purpose);
        Assert.True(await Challenges(context, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, result.Challenge!.Token));
        Assert.Equal(10, await context.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc != null));
        Assert.True((await context.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).TwoFactorEnabled);
    }

    [Fact]
    public async Task MatchingOrganizationEmail_NeverSuppliesPlatformCredentialsOrReceivesCounterUpdates()
    {
        var clock = new Clock();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var platform = await AccountAsync(email);
        var organization = await AccountAsync(email, "synthetic organization password", organization: true);
        Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock).BeginAsync(email, "synthetic organization password"));
        await using var reader = database.CreateContext();
        Assert.Equal(1, (await reader.Users.AsNoTracking().SingleAsync(row => row.Id == platform.Id)).AccessFailedCount);
        var org = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == organization.Id);
        Assert.Equal(0, org.AccessFailedCount);
        Assert.False(org.LockoutEnabled);
        var result = await Service(clock).BeginAsync(email, Password);
        Assert.True(result.ChallengeIssued);
        Assert.True(await Challenges(reader, clock).IsValidAsync(platform.PublicId, MfaChallengePurpose.Enrollment, result.Challenge!.Token));
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationOnlyOrNonadministrator_DoesNotReceiveChallengeOrCounterMutation(bool organization)
    {
        var clock = new Clock();
        var account = await AccountAsync(administrator: organization, organization: organization);
        Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock).BeginAsync(account.Email, Password));
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.Equal(0, loaded.AccessFailedCount);
        Assert.False(loaded.LockoutEnabled);
        Assert.Equal(account.PasswordHash, loaded.PasswordHash);
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Theory]
    [InlineData(null, "synthetic entry password 42")]
    [InlineData("not-an-email", "synthetic entry password 42")]
    [InlineData("unknown@example.test", null)]
    [InlineData("unknown@example.test", "")]
    [InlineData("unknown@example.test", "synthetic entry password 42")]
    public async Task InvalidOrUnknownInputs_UseTheSameEmptyRejection(string? email, string? password)
    {
        var result = await Service(new Clock()).BeginAsync(email, password);
        Assert.Same(PlatformPasswordEntryResult.Rejected, result);
        Assert.Null(result.Challenge);
        Assert.Null(result.Purpose);
    }

    [Fact]
    public async Task UnicodeWhitespaceAndExistingShortPasswords_AreVerifiedWithoutCreationRules()
    {
        var clock = new Clock();
        var account = await AccountAsync(password: "  synthetic password é  ");
        Assert.True((await Service(clock).BeginAsync(account.Email, "  synthetic password e\u0301  ")).ChallengeIssued);
        Assert.False((await Service(clock).BeginAsync(account.Email, "synthetic password é")).ChallengeIssued);
        Assert.False((await Service(clock).BeginAsync(account.Email, "  SYNTHETIC password é  ")).ChallengeIssued);
        var legacy = await AccountAsync(password: "password");
        Assert.True((await Service(clock).BeginAsync(legacy.Email, "password")).ChallengeIssued);
    }

    [Fact]
    public async Task FifthFailure_LocksForFifteenMinutes_RevokesNonceAndDoesNotExtendForRetries()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await using var reader = database.CreateContext();
        var previous = await Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        for (var index = 1; index <= 5; index++)
        {
            Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock).BeginAsync(account.Email, "wrong password"));
            var loaded = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
            Assert.Equal(index, loaded.AccessFailedCount);
            Assert.True(loaded.LockoutEnabled);
            if (index < 5)
                Assert.Null(loaded.LockoutEnd);
        }
        var blocked = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        var until = clock.Now.AddMinutes(15);
        Assert.Equal(until, blocked.LockoutEnd);
        Assert.NotNull((await reader.Set<MfaChallenge>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id)).RevokedAtUtc);
        Assert.False(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token));
        clock.Now = until.AddTicks(-1);
        Assert.False((await Service(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        Assert.False((await Service(clock).BeginAsync(account.Email, "wrong password")).ChallengeIssued);
        var unchanged = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.Equal(until, unchanged.LockoutEnd);
        Assert.Equal(5, unchanged.AccessFailedCount);
        Assert.Equal(blocked.ConcurrencyStamp, unchanged.ConcurrencyStamp);
        clock.Now = until;
        var fresh = await Service(clock).BeginAsync(account.Email, Password);
        Assert.True(fresh.ChallengeIssued);
        var reset = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.Equal(0, reset.AccessFailedCount);
        Assert.Null(reset.LockoutEnd);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, fresh.Challenge!.Token));
        Assert.False(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token));
    }

    [Fact]
    public async Task FailedPasswordAtLockoutExpiry_StartsAtOne()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        for (var index = 0; index < 5; index++)
            await Service(clock).BeginAsync(account.Email, "wrong");
        clock.Now = clock.Now.AddMinutes(15);
        Assert.False((await Service(clock).BeginAsync(account.Email, "wrong")).ChallengeIssued);
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.Equal(1, loaded.AccessFailedCount);
        Assert.Null(loaded.LockoutEnd);
    }

    [Fact]
    public async Task PasswordBudget_IsConsecutiveFailures_NotAFifteenMinuteFailureWindow()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        for (var index = 0; index < 5; index++)
        {
            await Service(clock).BeginAsync(account.Email, "wrong");
            clock.Now = clock.Now.AddDays(1);
        }
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.Equal(5, loaded.AccessFailedCount);
        Assert.NotNull(loaded.LockoutEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingActiveLockout_IsRespectedEvenWithDisabledLegacyFlag(bool enabled)
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await using var reader = database.CreateContext();
        var until = clock.Now.AddMinutes(30);
        await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.LockoutEnabled, enabled).SetProperty(row => row.LockoutEnd, (DateTimeOffset?)until));
        Assert.False((await Service(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        var loaded = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.True(loaded.LockoutEnabled);
        Assert.Equal(until, loaded.LockoutEnd);
        Assert.Equal(0, loaded.AccessFailedCount);
    }

    [Fact]
    public async Task PasswordSuccess_ResetsOnlyItsOwnCounter_AndReplacesTheOldEnrollmentChallenge()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await Service(clock).BeginAsync(account.Email, "wrong");
        await Service(clock).BeginAsync(account.Email, "wrong");
        await using var reader = database.CreateContext();
        var previous = await Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        var controls = new MfaVerificationService(new EfMfaVerificationStore(reader, clock));
        await controls.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token);
        await controls.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token);
        var before = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        var result = await Service(clock).BeginAsync(account.Email, Password);
        Assert.True(result.ChallengeIssued);
        Assert.False(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, previous.Token));
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, result.Challenge!.Token));
        Assert.Equal(0, (await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).AccessFailedCount);
        var after = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(before.FailedAttemptCount, after.FailedAttemptCount);
        Assert.Equal(before.FailureWindowStartedAtUtc, after.FailureWindowStartedAtUtc);
        Assert.Equal(before.LastAcceptedStep, after.LastAcceptedStep);
    }

    [Fact]
    public async Task CorrectPassword_CannotBypassOrResetMfaBlock()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await using var reader = database.CreateContext();
        var ticket = await Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        var controls = new MfaVerificationService(new EfMfaVerificationStore(reader, clock));
        for (var index = 0; index < 5; index++)
            await controls.RecordFailureAsync(account.PublicId, MfaChallengePurpose.Enrollment, ticket.Token);
        await Service(clock).BeginAsync(account.Email, "wrong");
        Assert.False((await Service(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        Assert.Equal(0, (await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).AccessFailedCount);
        var state = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(5, state.FailedAttemptCount);
        Assert.Equal(clock.Now.AddMinutes(15), state.BlockedUntilUtc);
        clock.Now = clock.Now.AddMinutes(15);
        Assert.True((await Service(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        var after = await reader.Set<MfaVerificationState>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(state.BlockedUntilUtc, after.BlockedUntilUtc);
        Assert.Equal(state.FailedAttemptCount, after.FailedAttemptCount);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task InconsistentMfaData_IsRejectedWithoutAdoptionOrRepair(bool enabled, bool credential, bool recovery)
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await using var reader = database.CreateContext();
        await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TwoFactorEnabled, enabled));
        if (credential)
            reader.Add(ConfirmedTotpCredential.Create(account.Id, _protector.Protect(account.PublicId, TotpSecret), clock.Now));
        if (recovery)
            reader.Add(MfaRecoveryCode.Create(account.Id, MfaRecoveryCodes.ComputeIssuedCodeDigest(account.PublicId, MfaRecoveryCodes.GenerateBatch()[0]), clock.Now));
        await reader.SaveChangesAsync();
        Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock).BeginAsync(account.Email, Password));
        Assert.Equal(enabled, (await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).TwoFactorEnabled);
        Assert.Equal(credential, await reader.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id));
        Assert.Equal(recovery, await reader.Set<MfaRecoveryCode>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyHash_IsUpgradedAtomically_AndChallengeBindsToNewStamp(bool versionTwo)
    {
        var clock = new Clock();
        var account = await AccountAsync(iterations: 10000, compatibility: versionTwo ? PasswordHasherCompatibilityMode.IdentityV2 : PasswordHasherCompatibilityMode.IdentityV3);
        await using var reader = database.CreateContext();
        var old = await Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        var result = await Service(clock).BeginAsync(account.Email, Password);
        Assert.True(result.ChallengeIssued);
        var loaded = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.NotEqual(account.PasswordHash, loaded.PasswordHash);
        Assert.NotEqual(account.SecurityStamp, loaded.SecurityStamp);
        Assert.Equal(PasswordVerificationResult.Success, Hasher().VerifyHashedPassword(loaded, loaded.PasswordHash!, Password));
        Assert.True(await Challenges(reader, clock).IsValidAsync(loaded.PublicId, MfaChallengePurpose.Enrollment, result.Challenge!.Token));
        Assert.False(await Challenges(reader, clock).IsValidAsync(loaded.PublicId, MfaChallengePurpose.Enrollment, old.Token));
        Assert.Equal(loaded.SecurityStamp, (await reader.Set<MfaChallenge>().AsNoTracking().SingleAsync(row => row.AccountId == account.Id && row.RevokedAtUtc == null)).SecurityStamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64-do-not-echo")]
    public async Task MissingOrMalformedStoredHash_FailsClosedWithoutEcho(string? hash)
    {
        var clock = new Clock();
        var account = await AccountAsync();
        await using var reader = database.CreateContext();
        await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordHash, hash));
        Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock).BeginAsync(account.Email, Password));
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
        Assert.Equal(0, (await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task ConcurrentWrongPasswords_CountFiveWithoutExtendingOrOverflowing()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Service(clock).BeginAsync(account.Email, "wrong")));
        Assert.All(results, result => Assert.Same(PlatformPasswordEntryResult.Rejected, result));
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.Equal(5, loaded.AccessFailedCount);
        Assert.Equal(clock.Now.AddMinutes(15), loaded.LockoutEnd);
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
    }

    [Fact]
    public async Task ConcurrentCorrectPasswords_LeaveOnlyTheLatestChallengeUsable()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service(clock).BeginAsync(account.Email, Password)));
        Assert.All(results, result => Assert.True(result.ChallengeIssued));
        await using var reader = database.CreateContext();
        var valid = new List<bool>();
        foreach (var result in results)
            valid.Add(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, result.Challenge!.Token));
        Assert.Single(valid, value => value);
        Assert.Equal(0, (await reader.Users.SingleAsync(row => row.Id == account.Id)).AccessFailedCount);
    }

    private sealed class ChangeBeforeLock(Func<Task> change) : DbCommandInterceptor
    {
        private int _changed;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[identity].[Accounts] WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _changed, 1) == 0)
                await change();
            return result;
        }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("stamp")]
    [InlineData("email")]
    [InlineData("claim")]
    public async Task CredentialOrRoleChangedAfterHashWork_CannotIssueFromStaleVerification(string change)
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var changedHash = Hasher().HashPassword(account, "changed synthetic password");
        var stamp = Guid.NewGuid().ToString("N");
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var normalizedEmail = AccountEmail.Create(email).NormalizedValue;
        var interceptor = new ChangeBeforeLock(async () =>
        {
            await using var writer = database.CreateContext();
            switch (change)
            {
                case "hash": await writer.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordHash, changedHash)); break;
                case "stamp": await writer.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, stamp)); break;
                case "email": await writer.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Email, email).SetProperty(row => row.NormalizedEmail, normalizedEmail)); break;
                case "claim": await writer.UserClaims.Where(row => row.UserId == account.Id).ExecuteDeleteAsync(); break;
            }
        });
        Assert.Same(PlatformPasswordEntryResult.Rejected, await Service(clock, interceptor).BeginAsync(account.Email, Password));
        await using var reader = database.CreateContext();
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
        Assert.Equal(0, (await reader.Users.SingleAsync(row => row.Id == account.Id)).AccessFailedCount);
    }

    private sealed class FailChallengeInsert : DbCommandInterceptor
    {
        public bool Attempted { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO [identity].[MfaChallenges]", StringComparison.Ordinal))
            {
                Attempted = true;
                throw new InvalidOperationException("Synthetic challenge persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ChallengeWriteFailure_RollsBackPasswordResetRehashStampsAndRevocation()
    {
        var clock = new Clock();
        var account = await AccountAsync(iterations: 10000);
        await using var reader = database.CreateContext();
        await reader.Users.Where(row => row.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.AccessFailedCount, 2));
        var old = await Challenges(reader, clock).IssueAsync(account.PublicId, account.SecurityStamp!, MfaChallengePurpose.Enrollment);
        var fault = new FailChallengeInsert();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(clock, fault).BeginAsync(account.Email, Password));
        Assert.True(fault.Attempted);
        var restored = await reader.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.Equal(account.PasswordHash, restored.PasswordHash);
        Assert.Equal(account.SecurityStamp, restored.SecurityStamp);
        Assert.Equal(account.ConcurrencyStamp, restored.ConcurrencyStamp);
        Assert.Equal(2, restored.AccessFailedCount);
        Assert.False(restored.LockoutEnabled);
        Assert.True(await Challenges(reader, clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Enrollment, old.Token));
        Assert.True((await Service(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
    }

    private sealed class FailSqlUpdate : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("[identity].[Accounts]", StringComparison.Ordinal))
                command.CommandText = "SELECT * FROM [identity].[SyntheticMissingPasswordEntryTable];";
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task SqlFailure_IsSanitized_AndInheritedSensitiveDiagnosticsAreDisabled()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        var messages = new List<string>();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(Options(new FailSqlUpdate()))
            .EnableSensitiveDataLogging().LogTo(messages.Add).Options;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformPasswordEntryService(options, clock).BeginAsync(account.Email, Password));
        Assert.Null(error.InnerException);
        Assert.Equal("Platform credential verification could not be completed.", error.Message);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain(account.Email!, error.ToString());
        Assert.DoesNotContain(account.PasswordHash!, error.ToString());
        Assert.Empty(messages);
    }

    [Fact]
    public async Task PreCancelledAttempt_DoesNotWriteOrIssueChallenge()
    {
        var clock = new Clock();
        var account = await AccountAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(clock).BeginAsync(account.Email, Password, cancellation.Token));
        await using var reader = database.CreateContext();
        Assert.False(await reader.Set<MfaChallenge>().AnyAsync(row => row.AccountId == account.Id));
        Assert.False((await reader.Users.SingleAsync(row => row.Id == account.Id)).LockoutEnabled);
    }
}
