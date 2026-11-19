using System.Security.Cryptography;
using GeoSurvey.ControlPlane.Infrastructure.Authentication;
using GeoSurvey.ControlPlane.Infrastructure.Mfa;
using GeoSurvey.ControlPlane.Infrastructure.SignIn;
using GeoSurvey.Identity.Application.Authentication;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Authentication;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Sessions;
using GeoSurvey.Organizations.Domain;
using GeoSurvey.Tenancy.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class AuthenticationLifecycleTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    private const string Password = "synthetic authentication integration password";
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private readonly ITotpSecretProtector _protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(89);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    // Exercises the auth module's contract boundary; physical tenant enforcement is tested in the tenancy suite.
    private sealed class Access : ITenantAdministratorAccess
    {
        public Guid Account { get; set; }
        public Guid Organization { get; set; }
        public bool Granted { get; set; }
        public Task<bool> CanAccessAsync(Guid accountPublicId, Guid organizationPublicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted && Account == accountPublicId && Organization == organizationPublicId);
    }

    private DbContextOptions<GlobalControlPlaneDbContext> Options(params IInterceptor[] interceptors)
    {
        using var context = database.CreateContext();
        return new DbContextOptionsBuilder<GlobalControlPlaneDbContext>(
            (DbContextOptions<GlobalControlPlaneDbContext>)context.GetService<IDbContextOptions>()).AddInterceptors(interceptors).Options;
    }
    private AuthenticationSessions Sessions(Clock clock, Access? access = null, params IInterceptor[] interceptors) => new(Options(interceptors), access ?? new Access(), clock);
    private PlatformPasswordEntryService PasswordEntry(Clock clock) => new(Options(), clock);

    private async Task<IdentityAccount> AccountAsync(Organization? organization = null, string? email = null)
    {
        await using var context = database.CreateContext();
        var account = organization is null ? IdentityAccount.CreatePlatformAccount(email ?? Guid.NewGuid().ToString("N") + "@example.test")
            : IdentityAccount.CreateOrganizationAccount(organization.Id, email ?? Guid.NewGuid().ToString("N") + "@example.test");
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        var settings = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(settings);
        account.PasswordHash = new PasswordHasher<IdentityAccount>(Microsoft.Extensions.Options.Options.Create(settings)).HashPassword(account, Password);
        context.Add(account);
        await context.SaveChangesAsync();
        if (organization is null)
        {
            context.UserClaims.Add(new IdentityUserClaim<int> { UserId = account.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
            await context.SaveChangesAsync();
        }
        return account;
    }

    private async Task<(IdentityAccount Account, IReadOnlyList<string> Codes)> EnrollAsync()
    {
        var account = await AccountAsync();
        var clock = new Clock { Now = DateTimeOffset.FromUnixTimeSeconds(59) };
        var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
        await using var context = database.CreateContext();
        Assert.True(await new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, clock), _protector, clock)
            .TryStoreAsync(account.PublicId, challenge.Token, Secret));
        var confirmed = await new MfaEnrollmentConfirmationService(context, _protector, clock).ConfirmAsync(account.PublicId, challenge.Token, "287082");
        Assert.True(confirmed.Succeeded);
        return (account, confirmed.RecoveryCodes);
    }

    private async Task<SessionGrant> RecoveryAsync(Clock clock, IdentityAccount account, string code)
    {
        var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
        return Assert.IsType<SessionGrant>(await Sessions(clock).SignInPlatformRecoveryAsync(challenge.Token, code));
    }

    [Fact]
    public async Task RecoveryAndRotation_KeepDigestEvidence_AndReuseRevokesOnlyItsFamily()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var first = await RecoveryAsync(clock, account, codes[0].ToLowerInvariant() + " ");
        var independent = await RecoveryAsync(clock, account, codes[1]);
        var service = Sessions(clock);
        Assert.NotNull(await service.ValidateAsync(account.PublicId, first.Subject.SessionPublicId, AuthenticationScope.Platform, null));
        Assert.Null(await service.RotateAsync(first.RefreshToken, AuthenticationScope.Organization));
        var rotated = Assert.IsType<SessionGrant>(await service.RotateAsync(first.RefreshToken, AuthenticationScope.Platform, first.Subject.SessionPublicId));
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
        Assert.Equal(first.SessionExpiresAtUtc, rotated.SessionExpiresAtUtc);
        await using var reader = database.CreateContext();
        Assert.Equal(1, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.CodeHash == MfaRecoveryCodes.ComputeIssuedCodeDigest(account.PublicId, codes[0]) && row.ConsumedAtUtc != null));
        Assert.False(await reader.Set<RefreshToken>().AnyAsync(row => row.TokenHash == first.RefreshToken || row.TokenHash == rotated.RefreshToken));
        Assert.Null(await service.RotateAsync(first.RefreshToken, AuthenticationScope.Platform));
        Assert.Null(await service.ValidateAsync(account.PublicId, first.Subject.SessionPublicId, AuthenticationScope.Platform, null));
        Assert.Null(await service.RotateAsync(rotated.RefreshToken, AuthenticationScope.Platform));
        Assert.NotNull(await service.ValidateAsync(account.PublicId, independent.Subject.SessionPublicId, AuthenticationScope.Platform, null));
        Assert.Equal(1, (await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id)).LastAcceptedStep);
    }

    [Fact]
    public async Task ConcurrentRefresh_StrictlyRevokesFamilyAfterSecondUse()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var grant = await RecoveryAsync(clock, account, codes[0]);
        var results = await Task.WhenAll(Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform),
            Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
        Assert.Single(results, value => value is not null);
        Assert.Null(await Sessions(clock).ValidateAsync(account.PublicId, grant.Subject.SessionPublicId, AuthenticationScope.Platform, null));
    }

    [Fact]
    public async Task SessionMismatch_DoesNotConsumeNewBrowserCookie_AndLogoutImmediatelyInvalidatesAccess()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var grant = await RecoveryAsync(clock, account, codes[0]);
        var service = Sessions(clock);
        await Assert.ThrowsAsync<AuthenticationSessionChangedException>(() => service.RevokeAsync(grant.RefreshToken, AuthenticationScope.Platform, Guid.NewGuid()));
        Assert.NotNull(await service.ValidateAsync(account.PublicId, grant.Subject.SessionPublicId, AuthenticationScope.Platform, null));
        await Assert.ThrowsAsync<AuthenticationSessionChangedException>(() => service.RotateAsync(grant.RefreshToken, AuthenticationScope.Platform, Guid.NewGuid()));
        Assert.NotNull(await service.RotateAsync(grant.RefreshToken, AuthenticationScope.Platform, grant.Subject.SessionPublicId));
        await service.RevokeAsync(grant.RefreshToken, AuthenticationScope.Platform);
        Assert.Null(await service.ValidateAsync(account.PublicId, grant.Subject.SessionPublicId, AuthenticationScope.Platform, null));
    }

    [Fact]
    public async Task RecoveryCodeReuseAndMalformedProof_SharePersistentMfaBudget()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        await RecoveryAsync(clock, account, codes[0]);
        var service = Sessions(clock);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
            Assert.Null(await service.SignInPlatformRecoveryAsync(challenge.Token, attempt % 2 == 0 ? codes[0] : "malformed"));
        }
        await using var reader = database.CreateContext();
        var state = await reader.Set<MfaVerificationState>().SingleAsync(row => row.AccountId == account.Id);
        Assert.Equal(5, state.FailedAttemptCount);
        Assert.Equal(clock.Now.AddMinutes(15), state.BlockedUntilUtc);
        Assert.False((await PasswordEntry(clock).BeginAsync(account.Email, Password)).ChallengeIssued);
        Assert.Equal(9, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc == null));
    }

    [Fact]
    public async Task FailedSessionWrite_RollsBackRecoveryAndChallengeConsumption()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sessions(clock, null, new RejectSessionSave()).SignInPlatformRecoveryAsync(challenge.Token, codes[0]));
        await using var reader = database.CreateContext();
        Assert.True(await new MfaChallengeService(new EfMfaChallengeStore(reader), clock).IsValidAsync(account.PublicId, MfaChallengePurpose.Verification, challenge.Token));
        Assert.Equal(10, await reader.Set<MfaRecoveryCode>().CountAsync(row => row.AccountId == account.Id && row.ConsumedAtUtc == null));
        Assert.False(await reader.Set<TokenFamily>().AnyAsync(row => row.AccountId == account.Id));
        Assert.NotNull(await Sessions(clock).SignInPlatformRecoveryAsync(challenge.Token, codes[0]));
    }

    private sealed class RejectSessionSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TokenFamily>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic failed session write.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task StampChangeAndRefreshInactivity_DenyExistingCredentials()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var grant = await RecoveryAsync(clock, account, codes[0]);
        await using var context = database.CreateContext();
        var stamp = Guid.NewGuid().ToString("N");
        await context.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.SecurityStamp, stamp));
        Assert.Null(await Sessions(clock).ValidateAsync(account.PublicId, grant.Subject.SessionPublicId, AuthenticationScope.Platform, null));
        Assert.Null(await Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
        var fresh = await RecoveryAsync(clock, account, codes[1]);
        clock.Now = clock.Now.AddMinutes(30);
        Assert.Null(await Sessions(clock).RotateAsync(fresh.RefreshToken, AuthenticationScope.Platform));
    }

    [Fact]
    public async Task OrganizationPassword_IsScopedAndRequiresCurrentGrant_OnEveryAccessAndRefresh()
    {
        var clock = new Clock();
        await using var context = database.CreateContext();
        var first = Organization.Create(OrganizationCode.Create("auth-" + Guid.NewGuid().ToString("N")), "First");
        var second = Organization.Create(OrganizationCode.Create("auth-" + Guid.NewGuid().ToString("N")), "Second");
        context.AddRange(first, second);
        await context.SaveChangesAsync();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var userA = await AccountAsync(first, email);
        var userB = await AccountAsync(second, email);
        var access = new Access { Account = userA.PublicId, Organization = first.PublicId, Granted = true };
        var service = Sessions(clock, access);
        Assert.Null(await service.SignInOrganizationAsync(second.Code.Value, email, Password));
        var session = Assert.IsType<SessionGrant>(await service.SignInOrganizationAsync(first.Code.Value, email, Password));
        Assert.Equal(userA.PublicId, session.Subject.AccountPublicId);
        Assert.NotEqual(userB.PublicId, session.Subject.AccountPublicId);
        Assert.Null(await service.ValidateAsync(userA.PublicId, session.Subject.SessionPublicId, AuthenticationScope.Organization, second.PublicId));
        Assert.NotNull(await service.ValidateAsync(userA.PublicId, session.Subject.SessionPublicId, AuthenticationScope.Organization, first.PublicId));
        access.Granted = false;
        Assert.Null(await service.ValidateAsync(userA.PublicId, session.Subject.SessionPublicId, AuthenticationScope.Organization, first.PublicId));
        Assert.Null(await service.RotateAsync(session.RefreshToken, AuthenticationScope.Organization));
    }

    [Fact]
    public async Task EnrollmentAdapter_GeneratesStoredSecretFromOpaqueProof_AndReusesOnlyItsPendingSecret()
    {
        var account = await AccountAsync();
        var clock = new Clock();
        var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
        var adapter = new PlatformEnrollmentService(Options(), _protector, clock);
        Assert.Null(await adapter.BeginAsync(OpaqueAuthenticationToken.Generate()));
        var material = Assert.IsType<EnrollmentMaterial>(await adapter.BeginAsync(challenge.Token));
        Assert.Equal(32, material.Secret.Length);
        Assert.Contains("otpauth://totp/", material.AuthenticatorUri);
        Assert.Equal(material.Secret, (await adapter.BeginAsync(challenge.Token))!.Secret);
        await using var context = database.CreateContext();
        var stored = await context.Set<PendingTotpEnrollment>().SingleAsync(row => row.AccountId == account.Id);
        Assert.DoesNotContain(material.Secret, stored.ProtectedSecret);
        Assert.DoesNotContain(material.Secret, material.ToString()!);
        Assert.Null(await adapter.ConfirmAsync(challenge.Token, "invalid"));
        Assert.False((await context.Users.AsNoTracking().SingleAsync(user => user.Id == account.Id)).TwoFactorEnabled);
    }

    [Fact]
    public async Task RefreshWriteFailure_RollsBackConsumption_AndRetainsUsableOriginalCredential()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var grant = await RecoveryAsync(clock, account, codes[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sessions(clock, null, new RejectRefreshSave())
            .RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
        await using var context = database.CreateContext();
        var hash = OpaqueAuthenticationToken.Digest(grant.RefreshToken);
        Assert.Null((await context.Set<RefreshToken>().SingleAsync(row => row.TokenHash == hash)).ConsumedAtUtc);
        Assert.NotNull(await Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
    }

    private sealed class RejectRefreshSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<RefreshToken>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic failed refresh write.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task RefreshActivity_CannotExtendOriginalEightHourDeadline()
    {
        var clock = new Clock();
        var (account, codes) = await EnrollAsync();
        var grant = await RecoveryAsync(clock, account, codes[0]);
        var deadline = grant.SessionExpiresAtUtc;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            clock.Now = clock.Now.AddMinutes(29);
            grant = Assert.IsType<SessionGrant>(await Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
            Assert.Equal(deadline, grant.SessionExpiresAtUtc);
        }
        Assert.Equal(deadline, grant.RefreshExpiresAtUtc);
        clock.Now = deadline;
        Assert.Null(await Sessions(clock).RotateAsync(grant.RefreshToken, AuthenticationScope.Platform));
        Assert.Null(await Sessions(clock).ValidateAsync(account.PublicId, grant.Subject.SessionPublicId, AuthenticationScope.Platform, null));
    }

    [Fact]
    public async Task EnrollmentConfirmation_RespectsFutureLegacyPasswordLockout()
    {
        var account = await AccountAsync();
        var clock = new Clock { Now = DateTimeOffset.FromUnixTimeSeconds(59) };
        var challenge = (await PasswordEntry(clock).BeginAsync(account.Email, Password)).Challenge!;
        await using var context = database.CreateContext();
        Assert.True(await new PendingTotpEnrollmentService(new EfPendingTotpEnrollmentStore(context, clock), _protector, clock)
            .TryStoreAsync(account.PublicId, challenge.Token, Secret));
        await context.Users.Where(user => user.Id == account.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.LockoutEnabled, false).SetProperty(user => user.LockoutEnd, clock.Now.AddMinutes(5)));
        Assert.Null(await new PlatformEnrollmentService(Options(), _protector, clock).ConfirmAsync(challenge.Token, "287082"));
        Assert.False(await context.Set<ConfirmedTotpCredential>().AnyAsync(row => row.AccountId == account.Id));
    }
}
