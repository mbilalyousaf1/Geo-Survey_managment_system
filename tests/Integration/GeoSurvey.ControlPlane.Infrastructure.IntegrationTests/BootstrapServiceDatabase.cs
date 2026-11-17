using System.Data.Common;
using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

public sealed class BootstrapServiceDatabase : MigrationDatabase, IAsyncDisposable
{
    private readonly ServiceProvider _operatorServices = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();

    public static async Task<BootstrapServiceDatabase> CreateAsync()
    {
        var database = new BootstrapServiceDatabase();
        try
        {
            await database.InitializeAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var administrator = CreateContext();
        await administrator.Database.ExecuteSqlRawAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bootstrap-role.sql")));
        await administrator.Database.ExecuteSqlRawAsync("""
            CREATE USER [BootstrapTestOperator] WITHOUT LOGIN;
            ALTER ROLE [GeoSurveyBootstrap] ADD MEMBER [BootstrapTestOperator];
            CREATE USER [NonBootstrapTestUser] WITHOUT LOGIN;
            """);
    }

    public DbContextOptions<GlobalControlPlaneDbContext> OperatorOptions(bool authorized = true)
    {
        using var administrator = CreateContext();
        return new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(administrator.Database.GetConnectionString()!, _operatorServices)
            .AddInterceptors(new ImpersonateTestUser(authorized)).Options;
    }

    public override async Task DisposeAsync()
    {
        try { await base.DisposeAsync(); }
        finally { await _operatorServices.DisposeAsync(); }
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    // Test-only real SQL Server permission contexts; no fake production authorization service.
    private sealed class ImpersonateTestUser(bool authorized) : DbConnectionInterceptor
    {
        private string Command => authorized
            ? "EXECUTE AS USER = N'BootstrapTestOperator';"
            : "EXECUTE AS USER = N'NonBootstrapTestUser';";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Command;
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Command;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
