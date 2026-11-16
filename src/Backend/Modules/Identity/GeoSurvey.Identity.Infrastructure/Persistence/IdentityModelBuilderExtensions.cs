using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

public static class IdentityModelBuilderExtensions
{
    /// <summary>Apply after IdentityUserContext base configuration. Does not compose the Organization FK.</summary>
    public static ModelBuilder ApplyGeoSurveyIdentityMappings(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new IdentityAccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountClaimConfiguration());
        modelBuilder.ApplyConfiguration(new AccountLoginConfiguration());
        modelBuilder.ApplyConfiguration(new AccountTokenConfiguration());
        return modelBuilder;
    }
}
