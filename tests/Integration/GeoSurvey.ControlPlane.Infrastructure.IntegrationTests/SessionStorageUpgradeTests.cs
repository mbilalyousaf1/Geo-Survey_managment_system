using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Sessions;
using GeoSurvey.Organizations.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class SessionStorageUpgradeTests(ConfirmedMfaMigrationDatabase database) : IClassFixture<ConfirmedMfaMigrationDatabase>
{
    [Fact]
    public async Task Upgrade_AddsEmptySessionStorage_PreservesExistingData_AndReapplyPreservesTokenHistory()
    {
        var created = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        await using var context = database.CreateContext();
        Assert.Equal(6, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var account = IdentityAccount.CreatePlatformAccount("session-upgrade@example.test");
        account.SecurityStamp = "synthetic-upgrade-stamp";
        account.TwoFactorEnabled = true;
        account.AccessFailedCount = 2;
        var hashOptions = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(hashOptions);
        account.PasswordHash = new PasswordHasher<IdentityAccount>(Options.Create(hashOptions)).HashPassword(account, "test-only-upgrade-password");
        var organization = Organization.Create(OrganizationCode.Create("session-upgrade"), "Upgrade Survey Team");
        var bootstrap = BootstrapCompletion.Create();
        context.AddRange(account, organization, bootstrap);
        await context.SaveChangesAsync();
        var organizationAccount = IdentityAccount.CreateOrganizationAccount(organization.Id, account.Email!);
        var challenge = MfaChallenge.Create(account.Id, MfaChallengePurpose.Verification, new string('A', 64), account.SecurityStamp, created, created.AddMinutes(10));
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var ciphertext = protector.Protect(account.PublicId, "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        context.AddRange(organizationAccount, challenge,
            ConfirmedTotpCredential.Create(account.Id, ciphertext, created),
            MfaRecoveryCode.Create(account.Id, new string('B', 64), created),
            new IdentityUserClaim<int> { UserId = account.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [identity].[MfaVerificationStates] ([AccountId], [FailedAttemptCount], [FailureWindowStartedAtUtc], [BlockedUntilUtc], [LastAcceptedStep]) VALUES ({account.Id}, 2, {created}, NULL, 123)");

        await context.Database.MigrateAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(8, (await reader.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await reader.Database.GetPendingMigrationsAsync());
        Assert.Empty(await reader.Set<TokenFamily>().ToListAsync());
        Assert.Empty(await reader.Set<RefreshToken>().ToListAsync());
        var loadedAccount = await reader.Users.SingleAsync(row => row.Id == account.Id);
        Assert.Equal(account.PublicId, loadedAccount.PublicId);
        Assert.Equal(AccountScope.Platform, loadedAccount.AccountScope);
        Assert.Null(loadedAccount.OrganizationId);
        Assert.Equal(account.PasswordHash, loadedAccount.PasswordHash);
        Assert.Equal(account.SecurityStamp, loadedAccount.SecurityStamp);
        Assert.Equal(account.ConcurrencyStamp, loadedAccount.ConcurrencyStamp);
        Assert.True(loadedAccount.TwoFactorEnabled);
        Assert.Equal(2, loadedAccount.AccessFailedCount);
        var loadedOrganizationAccount = await reader.Users.SingleAsync(row => row.Id == organizationAccount.Id);
        Assert.Equal(organizationAccount.PublicId, loadedOrganizationAccount.PublicId);
        Assert.Equal(AccountScope.Organization, loadedOrganizationAccount.AccountScope);
        Assert.Equal(organization.Id, loadedOrganizationAccount.OrganizationId);
        Assert.Equal(account.NormalizedEmail, loadedOrganizationAccount.NormalizedEmail);
        Assert.False(loadedOrganizationAccount.TwoFactorEnabled);
        var loadedOrganization = await reader.Organizations.SingleAsync();
        Assert.Equal(organization.PublicId, loadedOrganization.PublicId);
        Assert.Equal(organization.Code, loadedOrganization.Code);
        Assert.Equal(organization.CreatedAtUtc, loadedOrganization.CreatedAtUtc);
        Assert.Equal(bootstrap.CompletedAtUtc, (await reader.BootstrapCompletions.SingleAsync()).CompletedAtUtc);
        var credential = await reader.Set<ConfirmedTotpCredential>().SingleAsync();
        Assert.Equal(ciphertext, credential.ProtectedSecret);
        Assert.Equal(created, credential.ConfirmedAtUtc);
        var backup = await reader.Set<MfaRecoveryCode>().SingleAsync();
        Assert.Equal(account.Id, backup.AccountId);
        Assert.Equal(new string('B', 64), backup.CodeHash);
        Assert.Null(backup.ConsumedAtUtc);
        var control = await reader.Set<MfaVerificationState>().SingleAsync();
        Assert.Equal(2, control.FailedAttemptCount);
        Assert.Equal(123L, control.LastAcceptedStep);
        var loadedChallenge = await reader.Set<MfaChallenge>().SingleAsync();
        Assert.Equal(challenge.TokenHash, loadedChallenge.TokenHash);
        Assert.Equal(account.Id, loadedChallenge.AccountId);
        Assert.Null(loadedChallenge.ConsumedAtUtc);
        Assert.True(await reader.UserClaims.AnyAsync(row => row.UserId == account.Id && row.ClaimType == PlatformAdministratorClaims.Type && row.ClaimValue == PlatformAdministratorClaims.Administrator));

        // Synthetic storage rows exercise migration reapplication, not sign-in/rotation behavior.
        var family = TokenFamily.Create(account.Id, account.SecurityStamp, created, created.AddHours(1));
        reader.Add(family);
        await reader.SaveChangesAsync();
        var first = RefreshToken.Create(family.Id, new string('C', 64), created, created.AddMinutes(5));
        reader.Add(first);
        await reader.SaveChangesAsync();
        await reader.Set<RefreshToken>().Where(row => row.Id == first.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, created.AddMinutes(1)));
        var replacement = RefreshToken.Create(family.Id, new string('D', 64), created.AddMinutes(1), created.AddMinutes(10));
        reader.Add(replacement);
        await reader.SaveChangesAsync();
        await reader.Set<TokenFamily>().Where(row => row.Id == family.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, created.AddMinutes(2)));
        var history = (await reader.Database.GetAppliedMigrationsAsync()).ToArray();
        await reader.Database.MigrateAsync();
        await using var reloaded = database.CreateContext();
        Assert.Equal(history, await reloaded.Database.GetAppliedMigrationsAsync());
        var loadedFamily = await reloaded.Set<TokenFamily>().SingleAsync();
        Assert.Equal(family.PublicId, loadedFamily.PublicId);
        Assert.Equal(account.Id, loadedFamily.AccountId);
        Assert.Equal(account.SecurityStamp, loadedFamily.SecurityStamp);
        Assert.Equal(family.ExpiresAtUtc, loadedFamily.ExpiresAtUtc);
        Assert.Equal(created.AddMinutes(2), loadedFamily.RevokedAtUtc);
        Assert.Equal(2, await reloaded.Set<RefreshToken>().CountAsync());
        var consumed = await reloaded.Set<RefreshToken>().SingleAsync(row => row.Id == first.Id);
        Assert.Equal(first.TokenHash, consumed.TokenHash);
        Assert.Equal(created.AddMinutes(1), consumed.ConsumedAtUtc);
        Assert.Null((await reloaded.Set<RefreshToken>().SingleAsync(row => row.Id == replacement.Id)).ConsumedAtUtc);
        Assert.Equal(account.PasswordHash, (await reloaded.Users.SingleAsync(row => row.Id == account.Id)).PasswordHash);
        Assert.Equal(ciphertext, (await reloaded.Set<ConfirmedTotpCredential>().SingleAsync()).ProtectedSecret);
    }
}
