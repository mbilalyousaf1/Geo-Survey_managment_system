using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Domain;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class BootstrapCompletionUpgradeTests(InitialMigrationDatabase database) : IClassFixture<InitialMigrationDatabase>
{
    [Fact]
    public async Task Upgrade_PreservesExistingDataAndMarkerSurvivesAccountDeletion()
    {
        await using var context = database.CreateContext();
        Assert.EndsWith("_InitialGlobalControlPlane", Assert.Single(await context.Database.GetAppliedMigrationsAsync()));
        var markerTable = await context.Database.SqlQuery<int?>(
            $"SELECT OBJECT_ID(N'[controlplane].[BootstrapCompletion]', N'U') AS [Value]").SingleAsync();
        Assert.Null(markerTable);
        var organization = Organization.Create(OrganizationCode.Create("upgrade-test"), "Upgrade Survey Team");
        context.Add(organization);
        await context.SaveChangesAsync();
        var account = IdentityAccount.CreateOrganizationAccount(organization.Id, "upgrade@example.test");
        context.Add(account);
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();
        Assert.Equal(5, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.BootstrapCompletions.ToListAsync());
        var completion = BootstrapCompletion.Create();
        context.Add(completion);
        await context.SaveChangesAsync();
        await using var reader = database.CreateContext();
        Assert.Equal(organization.PublicId, (await reader.Organizations.SingleAsync()).PublicId);
        Assert.Equal(account.PublicId, (await reader.Users.SingleAsync()).PublicId);
        // Storage-only test in a disposable database, not approval of an administrator deletion workflow.
        await reader.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[Accounts] WHERE [Id] = {account.Id}");
        Assert.Empty(await reader.Users.ToListAsync());
        Assert.Equal(completion.CompletedAtUtc, (await reader.BootstrapCompletions.SingleAsync()).CompletedAtUtc);
        await reader.Database.MigrateAsync();
        Assert.Single(await reader.BootstrapCompletions.ToListAsync());
        Assert.Empty(await reader.Database.GetPendingMigrationsAsync());
    }
}
