using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class ControlPlaneMappingTests(ControlPlaneDatabase database) : IClassFixture<ControlPlaneDatabase>
{
    private static string NewEmail() => "user-" + Guid.NewGuid().ToString("N") + "@example.test";

    private async Task<Organization> PersistOrganizationAsync()
    {
        var organization = Organization.Create(OrganizationCode.Create("org-" + Guid.NewGuid().ToString("N")), "Survey Team");
        await using var context = database.CreateContext();
        context.Add(organization);
        await context.SaveChangesAsync();
        return organization;
    }

    [Fact]
    public async Task OrganizationAndPlatformAccounts_PersistAndReloadWithCorrectAssociation()
    {
        var organization = await PersistOrganizationAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, NewEmail());
        var platform = IdentityAccount.CreatePlatformAccount(NewEmail());
        await using (var writer = database.CreateContext())
        {
            writer.AddRange(account, platform);
            await writer.SaveChangesAsync();
        }
        await using var reader = database.CreateContext();
        var loaded = await reader.Users.SingleAsync(item => item.PublicId == account.PublicId);
        Assert.Equal(account.Id, loaded.Id);
        Assert.Equal(organization.Id, loaded.OrganizationId);
        Assert.Equal(AccountScope.Organization, loaded.AccountScope);
        Assert.Equal(account.NormalizedEmail, loaded.NormalizedEmail);
        Assert.Equal(organization.PublicId, (await reader.Organizations.SingleAsync(item => item.Id == loaded.OrganizationId)).PublicId);
        var loadedPlatform = await reader.Users.SingleAsync(item => item.PublicId == platform.PublicId);
        Assert.Equal(AccountScope.Platform, loadedPlatform.AccountScope);
        Assert.Null(loadedPlatform.OrganizationId);
    }

    [Fact]
    public async Task MissingOrganization_InsertIsRejected()
    {
        await using var context = database.CreateContext();
        context.Add(IdentityAccount.CreateOrganizationAccount(int.MaxValue, NewEmail()));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
    }

    [Fact]
    public async Task MissingOrganization_UpdateIsRejectedAndOriginalAssociationSurvives()
    {
        var organization = await PersistOrganizationAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, NewEmail());
        await using (var writer = database.CreateContext())
        {
            writer.Add(account);
            await writer.SaveChangesAsync();
            writer.Entry(account).Property(item => item.OrganizationId).CurrentValue = int.MaxValue;
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
        }
        await using var reader = database.CreateContext();
        Assert.Equal(organization.Id, (await reader.Users.SingleAsync(item => item.Id == account.Id)).OrganizationId);
    }

    [Fact]
    public async Task OrganizationDeletion_WithAccountIsBlockedWithoutCascade()
    {
        var organization = await PersistOrganizationAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, NewEmail());
        await using var context = database.CreateContext();
        context.Add(account);
        await context.SaveChangesAsync();
        // Execute SQL directly so EF tracking behavior cannot mask server-side FK enforcement.
        var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [organization].[Organizations] WHERE [Id] = {organization.Id}"));
        Assert.Equal(547, exception.Number);
        Assert.True(await context.Organizations.AnyAsync(item => item.Id == organization.Id));
        Assert.True(await context.Users.AnyAsync(item => item.Id == account.Id));
    }

    [Fact]
    public async Task EmailReuseAcrossRealOrganizationsAndPlatform_IsAllowed()
    {
        var first = await PersistOrganizationAsync();
        var second = await PersistOrganizationAsync();
        var email = NewEmail();
        await using var context = database.CreateContext();
        context.AddRange(IdentityAccount.CreateOrganizationAccount(first.Id, email),
            IdentityAccount.CreateOrganizationAccount(second.Id, email.ToUpperInvariant()),
            IdentityAccount.CreatePlatformAccount(email));
        Assert.Equal(3, await context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentScopedEmailDuplicates_ExactlyOneSucceeds(bool organizationScope)
    {
        var organization = organizationScope ? await PersistOrganizationAsync() : null;
        var email = NewEmail();
        IdentityAccount Create(string value) => organization is null
            ? IdentityAccount.CreatePlatformAccount(value)
            : IdentityAccount.CreateOrganizationAccount(organization.Id, value);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        first.Add(Create(email));
        second.Add(Create(email.ToUpperInvariant()));

        static async Task<bool> SaveAsync(GlobalControlPlaneDbContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }
        var results = await Task.WhenAll(SaveAsync(first), SaveAsync(second));
        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
    }

    [Fact]
    public async Task ComposedDatabase_ContainsOnlyFiveReviewedTables()
    {
        await using var context = database.CreateContext();
        var tables = await context.Database.SqlQuery<string>(
            $"SELECT [s].[name] + '.' + [t].[name] AS [Value] FROM [sys].[tables] AS [t] INNER JOIN [sys].[schemas] AS [s] ON [s].[schema_id] = [t].[schema_id] WHERE [t].[is_ms_shipped] = 0").ToListAsync();
        Assert.Equal(new[] { "identity.AccountClaims", "identity.AccountLogins", "identity.AccountTokens", "identity.Accounts", "organization.Organizations" },
            tables.Order(StringComparer.Ordinal));
        Assert.Null(context.Model.FindEntityType(typeof(IdentityRole<int>)));
        Assert.Null(context.Model.FindEntityType(typeof(IdentityUserPasskey<int>)));
        var account = context.Model.FindEntityType(typeof(IdentityAccount))!;
        var foreignKey = Assert.Single(account.GetForeignKeys());
        Assert.Equal(typeof(Organization), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(IdentityAccount.OrganizationId), Assert.Single(foreignKey.Properties).Name);
        Assert.False(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior);
        Assert.Empty(account.GetNavigations());
        Assert.Empty(context.Model.FindEntityType(typeof(Organization))!.GetNavigations());
        Assert.Equal(256, account.FindProperty(nameof(IdentityAccount.PhoneNumber))!.GetMaxLength());
    }

    [Fact]
    public void MigrationHistory_UsesApprovedSchemaWithoutApplyingMigrations()
    {
        using var context = database.CreateContext();
        var historyScript = context.GetService<IHistoryRepository>().GetCreateScript();
        Assert.Contains("[controlplane].[__EFMigrationsHistory]", historyScript, StringComparison.Ordinal);
        Assert.EndsWith("_InitialGlobalControlPlane", Assert.Single(context.Database.GetMigrations()));
    }
}
