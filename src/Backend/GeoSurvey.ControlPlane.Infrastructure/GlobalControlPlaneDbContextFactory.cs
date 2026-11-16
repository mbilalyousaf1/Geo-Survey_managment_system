using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.ControlPlane.Infrastructure;

/// <summary>Creates the model for EF tooling without starting the API or opening a connection.</summary>
public sealed class GlobalControlPlaneDbContextFactory : IDesignTimeDbContextFactory<GlobalControlPlaneDbContext>
{
    public const string ConnectionEnvironmentVariable = "GEOSURVEY_DESIGNTIME_SQLSERVER";

    // Only fixed, nonsensitive Identity options live here, shared for the tooling process lifetime.
    private static readonly Lazy<ServiceProvider> Services = new(() => new ServiceCollection()
        .Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider());

    public GlobalControlPlaneDbContext CreateDbContext(string[] args)
    {
        if (args.Length != 0)
            throw new ArgumentException("Design-time factory arguments are not supported. Supply connection configuration through the designated environment variable.", nameof(args));

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Set {ConnectionEnvironmentVariable} for EF tooling. No default connection is configured.");

        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(connectionString, Services.Value).Options;
        return new GlobalControlPlaneDbContext(options);
    }
}
