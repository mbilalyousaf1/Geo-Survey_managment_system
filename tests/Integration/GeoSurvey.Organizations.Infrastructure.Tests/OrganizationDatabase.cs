using GeoSurvey.Organizations.Domain;
using GeoSurvey.Organizations.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.Organizations.Infrastructure.Tests;

// This context is deliberately test-only: global composition and migrations are a later task.
public sealed class OrganizationTestContext(DbContextOptions<OrganizationTestContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
}

public sealed class OrganizationDatabase : IAsyncLifetime
{
    private DbContextOptions<OrganizationTestContext> _options = null!;
    private bool _databaseCreated;

    public OrganizationTestContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("GEOSURVEY_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Run scripts/Test-Backend.ps1 to supply an isolated SQL Server. Database tests are never silently skipped.");

        var connection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "GeoSurvey_OrganizationTests_" + Guid.NewGuid().ToString("N"),
            ConnectTimeout = 2
        };
        _options = new DbContextOptionsBuilder<OrganizationTestContext>().UseSqlServer(connection.ConnectionString).Options;

        // Wait for SQL Server startup without logging credentials or provider error details.
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
        if (!_databaseCreated)
            return;
        await using var context = CreateContext();
        // Only the randomly named database created by this fixture can be deleted.
        await context.Database.EnsureDeletedAsync();
    }
}
