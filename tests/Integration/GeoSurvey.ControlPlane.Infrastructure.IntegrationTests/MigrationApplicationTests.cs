using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class MigrationApplicationTests(MigrationDatabase database) : IClassFixture<MigrationDatabase>
{
    public sealed class HistoryRow
    {
        public string MigrationId { get; set; } = string.Empty;
        public string ProductVersion { get; set; } = string.Empty;
    }

    private static string NewEmail() => "user-" + Guid.NewGuid().ToString("N") + "@example.test";

    private async Task<Organization> PersistOrganizationAsync()
    {
        var organization = Organization.Create(OrganizationCode.Create("org-" + Guid.NewGuid().ToString("N")), "Migration Survey Team");
        await using var context = database.CreateContext();
        context.Add(organization);
        await context.SaveChangesAsync();
        return organization;
    }

    private static Task<List<HistoryRow>> ReadHistoryAsync(GlobalControlPlaneDbContext context) =>
        context.Database.SqlQuery<HistoryRow>(
            $"SELECT [MigrationId], [ProductVersion] FROM [controlplane].[__EFMigrationsHistory]").ToListAsync();

    [Fact]
    public async Task FreshMigration_CreatesReviewedTablesHistoryAndNoActionForeignKeys()
    {
        Assert.True(database.StartedWithoutDatabase);
        await using var context = database.CreateContext();
        var expectedMigrations = context.Database.GetMigrations().ToArray();
        Assert.Equal(8, expectedMigrations.Length);
        Assert.EndsWith("_InitialGlobalControlPlane", expectedMigrations[0]);
        Assert.EndsWith("_AddBootstrapCompletion", expectedMigrations[1]);
        Assert.EndsWith("_AddMfaChallenges", expectedMigrations[2]);
        Assert.EndsWith("_AddPendingTotpEnrollments", expectedMigrations[3]);
        Assert.EndsWith("_AddMfaVerificationStates", expectedMigrations[4]);
        Assert.EndsWith("_AddConfirmedMfaEnrollment", expectedMigrations[5]);
        Assert.EndsWith("_AddTokenFamiliesAndRefreshTokens", expectedMigrations[6]);
        Assert.EndsWith("_AddTenantProvisioningAndActivation", expectedMigrations[7]);
        Assert.Equal(expectedMigrations, await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var rows = await ReadHistoryAsync(context);
        Assert.Equal(expectedMigrations, rows.OrderBy(row => row.MigrationId).Select(row => row.MigrationId));
        Assert.All(rows, row => Assert.Equal("10.0.9", row.ProductVersion));
        var tables = await context.Database.SqlQuery<string>(
            $"SELECT [s].[name] + '.' + [t].[name] AS [Value] FROM [sys].[tables] AS [t] INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] WHERE [t].[is_ms_shipped] = 0").ToListAsync();
        Assert.Equal(new[] { "audit.TenantLifecycleAudit", "controlplane.BootstrapCompletion", "controlplane.__EFMigrationsHistory", "identity.AccountClaims", "identity.AccountLogins", "identity.AccountTokens", "identity.Accounts", "identity.MfaChallenges", "identity.MfaRecoveryCodes", "identity.MfaVerificationStates", "identity.PendingTotpEnrollments", "identity.RefreshTokens", "identity.TokenFamilies", "identity.TotpCredentials", "organization.Organizations", "tenancy.AdministratorInvitations", "tenancy.ProvisioningOutbox", "tenancy.TenantRegistry" },
            tables.Order(StringComparer.Ordinal));
        var foreignKeyActions = await context.Database.SqlQuery<int>(
            $"SELECT CAST([delete_referential_action] AS int) AS [Value] FROM [sys].[foreign_keys]").ToListAsync();
        Assert.Equal(17, foreignKeyActions.Count);
        Assert.All(foreignKeyActions, action => Assert.Equal(0, action));
    }

    [Fact]
    public async Task Reapply_PreservesExistingRowsAndHistoryEntries()
    {
        var organization = await PersistOrganizationAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, NewEmail());
        List<HistoryRow> before;
        await using (var writer = database.CreateContext())
        {
            writer.Add(account);
            await writer.SaveChangesAsync();
            before = await ReadHistoryAsync(writer);
        }
        await using (var migrator = database.CreateContext())
            await migrator.Database.MigrateAsync();

        await using var reader = database.CreateContext();
        var loadedOrganization = await reader.Organizations.SingleAsync(item => item.PublicId == organization.PublicId);
        Assert.Equal(organization.Id, loadedOrganization.Id);
        Assert.Equal(organization.Code, loadedOrganization.Code);
        Assert.Equal(organization.DisplayName, loadedOrganization.DisplayName);
        Assert.Equal(organization.CreatedAtUtc, loadedOrganization.CreatedAtUtc);
        var loadedAccount = await reader.Users.SingleAsync(item => item.PublicId == account.PublicId);
        Assert.Equal(account.Id, loadedAccount.Id);
        Assert.Equal(account.OrganizationId, loadedAccount.OrganizationId);
        Assert.Equal(account.AccountScope, loadedAccount.AccountScope);
        Assert.Equal(account.Email, loadedAccount.Email);
        Assert.Equal(account.NormalizedEmail, loadedAccount.NormalizedEmail);
        Assert.Equal(account.ConcurrencyStamp, loadedAccount.ConcurrencyStamp);
        var after = await ReadHistoryAsync(reader);
        Assert.Equal(8, after.Count);
        Assert.Equal(before.OrderBy(row => row.MigrationId).Select(row => (row.MigrationId, row.ProductVersion)),
            after.OrderBy(row => row.MigrationId).Select(row => (row.MigrationId, row.ProductVersion)));
        Assert.Empty(await reader.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task MigratedDatabase_RejectsAccountReferencingMissingOrganization()
    {
        await using var context = database.CreateContext();
        context.Add(IdentityAccount.CreateOrganizationAccount(int.MaxValue, NewEmail()));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Fact]
    public async Task MigratedDatabase_BlocksOrganizationDeletionWithoutDeletingAccounts()
    {
        var organization = await PersistOrganizationAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, NewEmail());
        await using var context = database.CreateContext();
        context.Add(account);
        await context.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [organization].[Organizations] WHERE [Id] = {organization.Id}"));
        Assert.Equal(547, exception.Number);
        Assert.True(await context.Users.AnyAsync(item => item.Id == account.Id));
        Assert.True(await context.Organizations.AnyAsync(item => item.Id == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigratedDatabase_AllowsCrossScopeReuseAndRejectsScopedDuplicates(bool duplicateOrganizationAccount)
    {
        var first = await PersistOrganizationAsync();
        var second = await PersistOrganizationAsync();
        var email = NewEmail();
        await using var context = database.CreateContext();
        context.AddRange(IdentityAccount.CreateOrganizationAccount(first.Id, email),
            IdentityAccount.CreateOrganizationAccount(second.Id, email.ToUpperInvariant()),
            IdentityAccount.CreatePlatformAccount(email));
        Assert.Equal(3, await context.SaveChangesAsync());
        context.Add(duplicateOrganizationAccount
            ? IdentityAccount.CreateOrganizationAccount(first.Id, email.ToUpperInvariant())
            : IdentityAccount.CreatePlatformAccount(email.ToUpperInvariant()));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, new[] { 2601, 2627 });
    }
}
