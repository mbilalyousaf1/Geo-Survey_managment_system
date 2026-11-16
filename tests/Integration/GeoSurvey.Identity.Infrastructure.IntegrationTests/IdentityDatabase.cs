using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.Identity.Infrastructure.IntegrationTests;

// Deliberately excludes Organizations: its FK belongs to the future ControlPlane context.
public sealed class IdentityTestContext(DbContextOptions<IdentityTestContext> options)
    : IdentityUserContext<IdentityAccount, int>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyGeoSurveyIdentityMappings();
    }
}

public sealed class IdentityDatabase : IAsyncLifetime
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
    private DbContextOptions<IdentityTestContext> _options = null!;
    private bool _databaseCreated;

    public IdentityTestContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("GEOSURVEY_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Run scripts/Test-Backend.ps1 to supply isolated SQL Server. Database tests are never silently skipped.");

        var connection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "GeoSurvey_IdentityTests_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 2
        };
        _options = new DbContextOptionsBuilder<IdentityTestContext>()
            .UseSqlServer(connection.ConnectionString).UseApplicationServiceProvider(_services).Options;

        connection.InitialCatalog = "master";
        var ready = false;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var probe = new SqlConnection(connection.ConnectionString);
                await probe.OpenAsync();
                ready = true;
                break;
            }
            catch (SqlException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        if (!ready)
            throw new InvalidOperationException("Isolated test SQL Server did not become ready within the startup deadline.");

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        _databaseCreated = true;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_databaseCreated)
            {
                await using var context = CreateContext();
                await context.Database.EnsureDeletedAsync();
            }
        }
        finally
        {
            await _services.DisposeAsync();
        }
    }
}
