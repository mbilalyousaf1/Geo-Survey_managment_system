using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure;

public static class GlobalControlPlaneOptions
{
    public const string MigrationHistorySchema = "controlplane";
    public const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <summary>Configure using trusted server-side connection data. Does not connect or migrate.</summary>
    public static DbContextOptionsBuilder<GlobalControlPlaneDbContext> UseGeoSurveyControlPlaneSqlServer(
        this DbContextOptionsBuilder<GlobalControlPlaneDbContext> builder,
        string connectionString,
        IServiceProvider applicationServices)
    {
        ((DbContextOptionsBuilder)builder).UseGeoSurveyControlPlaneSqlServer(connectionString, applicationServices);
        return builder;
    }

    /// <summary>Same configuration for EF's standard AddDbContext callback.</summary>
    public static DbContextOptionsBuilder UseGeoSurveyControlPlaneSqlServer(
        this DbContextOptionsBuilder builder,
        string connectionString,
        IServiceProvider applicationServices)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(applicationServices);
        ValidateIdentityOptions(applicationServices.GetRequiredService<IOptions<IdentityOptions>>().Value);

        return builder.UseApplicationServiceProvider(applicationServices)
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(MigrationHistoryTable, MigrationHistorySchema));
    }

    internal static void ValidateIdentityOptions(IdentityOptions options)
    {
        if (options.Stores.SchemaVersion != IdentitySchemaVersions.Version2
            || options.Stores.MaxLengthForKeys != IdentityPersistenceOptions.MaximumKeyLength)
            throw new InvalidOperationException("Configure IdentityPersistenceOptions before building the global context: Identity schema Version 2 and key length 128 are required.");
    }
}
