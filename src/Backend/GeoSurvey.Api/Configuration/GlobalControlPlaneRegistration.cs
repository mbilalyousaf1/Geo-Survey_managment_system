using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Api.Configuration;

public static class GlobalControlPlaneRegistration
{
    public static IServiceCollection AddGlobalControlPlanePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GlobalControlPlaneConnectionOptions>()
            .Configure(options => options.ConnectionString = configuration.GetConnectionString("GlobalControlPlane"))
            .Validate(options => IsValidConnection(options.ConnectionString),
                "ConnectionStrings:GlobalControlPlane must contain a valid SQL Server connection string with an explicit Server and Database. Supply it through development user-secrets or environment configuration.")
            .ValidateOnStart();
        services.Configure<IdentityOptions>(IdentityPersistenceOptions.Configure);
        services.AddDbContext<GlobalControlPlaneDbContext>((provider, options) =>
            options.UseGeoSurveyControlPlaneSqlServer(
                provider.GetRequiredService<IOptions<GlobalControlPlaneConnectionOptions>>().Value.ConnectionString!, provider));
        return services;
    }

    private static bool IsValidConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;
        try
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(parsed.DataSource) && !string.IsNullOrWhiteSpace(parsed.InitialCatalog);
        }
        catch (ArgumentException)
        {
            // Parser errors may contain supplied values; return only the fixed validation message.
            return false;
        }
    }
}

internal sealed class GlobalControlPlaneConnectionOptions
{
    public string? ConnectionString { get; set; }
}
