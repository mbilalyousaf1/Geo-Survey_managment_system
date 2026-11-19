using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.ControlPlane.Infrastructure.IntegrationTests;

/// <summary>Starts without a database and applies real migrations, never EnsureCreated.</summary>
public class MigrationDatabase : IAsyncLifetime
{
    private readonly string? _targetMigration;
    private readonly ServiceProvider _services = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
    private DbContextOptions<GlobalControlPlaneDbContext> _options = null!;
    private bool _cleanupRequired;

    public MigrationDatabase() : this(null) { }

    protected MigrationDatabase(string? targetMigration) => _targetMigration = targetMigration;

    public bool StartedWithoutDatabase { get; private set; }

    public GlobalControlPlaneDbContext CreateContext() => new(_options);

    public virtual async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("GEOSURVEY_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Run scripts/Test-Backend.ps1 to supply isolated SQL Server. Migration tests are never silently skipped.");

        var databaseName = "GeoSurvey_MigrationTests_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
            ConnectTimeout = 2
        };
        _options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(connection.ConnectionString, _services).Options;

        connection.InitialCatalog = "master";
        var ready = false;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var probe = new SqlConnection(connection.ConnectionString);
                await probe.OpenAsync();
                await using var command = new SqlCommand("SELECT DB_ID(@databaseName)", probe);
                command.Parameters.AddWithValue("@databaseName", databaseName);
                StartedWithoutDatabase = await command.ExecuteScalarAsync() is DBNull;
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
        if (!StartedWithoutDatabase)
            throw new InvalidOperationException("Migration test target unexpectedly exists; refusing to modify or delete it.");

        // A failed migration can leave a partially created database; cleanup remains restricted to our new name.
        _cleanupRequired = true;
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(_targetMigration);
    }

    public virtual async Task DisposeAsync()
    {
        try
        {
            if (_cleanupRequired)
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

public sealed class InitialMigrationDatabase() : MigrationDatabase("20261116114419_InitialGlobalControlPlane");

public sealed class MfaChallengeMigrationDatabase() : MigrationDatabase("20261117102009_AddMfaChallenges");

public sealed class PendingEnrollmentMigrationDatabase() : MigrationDatabase("20261117112151_AddPendingTotpEnrollments");

public sealed class MfaVerificationMigrationDatabase() : MigrationDatabase("20261117115011_AddMfaVerificationStates");

public sealed class ConfirmedMfaMigrationDatabase() : MigrationDatabase("20261117174212_AddConfirmedMfaEnrollment");
