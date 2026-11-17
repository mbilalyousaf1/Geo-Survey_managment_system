using GeoSurvey.Identity.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

[CollectionDefinition("Design-time environment", DisableParallelization = true)]
public sealed class DesignTimeEnvironmentCollection;

[Collection("Design-time environment")]
public sealed class DesignTimeFactoryTests
{
    // No server listens here; these tests create a model and generate SQL without connecting.
    private const string OfflineConnection = "Server=127.0.0.1,1;Database=GeoSurvey_DesignTimeModel;Integrated Security=True;Encrypt=True;Connect Timeout=1";

    private static void WithConnection(string? connection, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(GlobalControlPlaneDbContextFactory.ConnectionEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(GlobalControlPlaneDbContextFactory.ConnectionEnvironmentVariable, connection);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(GlobalControlPlaneDbContextFactory.ConnectionEnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void MissingConnection_ExplainsRequiredConfiguration(string? connection)
    {
        WithConnection(connection, () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new GlobalControlPlaneDbContextFactory().CreateDbContext([]));
            Assert.Contains(GlobalControlPlaneDbContextFactory.ConnectionEnvironmentVariable, exception.Message);
        });
    }

    [Fact]
    public void Arguments_AreRejectedWithoutEchoingTheirContents()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GlobalControlPlaneDbContextFactory().CreateDbContext(["do-not-echo-input"]));
        Assert.DoesNotContain("do-not-echo-input", exception.Message);
    }

    [Fact]
    public void Factory_ReusesProviderSchemaAndHistoryWithoutConnecting()
    {
        WithConnection(OfflineConnection, () =>
        {
            using var context = new GlobalControlPlaneDbContextFactory().CreateDbContext([]);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
            Assert.Equal(9, context.Model.GetEntityTypes().Count());
            Assert.Null(context.Model.FindEntityType(typeof(IdentityUserPasskey<int>)));
            Assert.Equal(256, context.Model.FindEntityType(typeof(IdentityAccount))!.FindProperty(nameof(IdentityAccount.PhoneNumber))!.GetMaxLength());
            Assert.Contains("[controlplane].[__EFMigrationsHistory]", context.GetService<IHistoryRepository>().GetCreateScript());
        });
    }

    [Fact]
    public void MigrationSnapshot_MatchesFactoryModelAndSqlContainsApprovedConstraints()
    {
        WithConnection(OfflineConnection, () =>
        {
            using var context = new GlobalControlPlaneDbContextFactory().CreateDbContext([]);
            var migrations = context.Database.GetMigrations().ToArray();
            Assert.Equal(5, migrations.Length);
            Assert.EndsWith("_InitialGlobalControlPlane", migrations[0]);
            Assert.EndsWith("_AddBootstrapCompletion", migrations[1]);
            Assert.EndsWith("_AddMfaChallenges", migrations[2]);
            Assert.EndsWith("_AddPendingTotpEnrollments", migrations[3]);
            Assert.EndsWith("_AddMfaVerificationStates", migrations[4]);
            Assert.False(context.Database.HasPendingModelChanges());
            var sql = context.GetService<IMigrator>().GenerateScript();
            foreach (var table in new[] { "Accounts", "AccountClaims", "AccountLogins", "AccountTokens", "MfaChallenges", "MfaVerificationStates", "PendingTotpEnrollments" })
                Assert.Contains($"CREATE TABLE [identity].[{table}]", sql);
            Assert.Contains("CREATE TABLE [organization].[Organizations]", sql);
            Assert.Contains("CREATE TABLE [controlplane].[BootstrapCompletion]", sql);
            Assert.Contains("[CK_BootstrapCompletion_Singleton]", sql);
            Assert.Contains("[CK_Accounts_ScopeOrganization]", sql);
            Assert.Contains("[UX_Accounts_OrganizationEmail]", sql);
            Assert.Contains("WHERE [AccountScope] = 2", sql);
            Assert.Contains("WHERE [AccountScope] = 1", sql);
            Assert.Contains("REFERENCES [organization].[Organizations] ([Id])", sql);
            Assert.DoesNotContain("ON DELETE CASCADE", sql);
            Assert.DoesNotContain("AspNetRoles", sql);
            Assert.DoesNotContain("Passkeys", sql);
        });
    }
}
