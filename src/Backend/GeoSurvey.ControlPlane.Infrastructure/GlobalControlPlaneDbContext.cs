using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Persistence;
using GeoSurvey.Organizations.Domain;
using GeoSurvey.Organizations.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure;

/// <summary>Global identity/organization storage composition; never a tenant operational context.</summary>
public sealed class GlobalControlPlaneDbContext : IdentityUserContext<IdentityAccount, int>
{
    public GlobalControlPlaneDbContext(DbContextOptions<GlobalControlPlaneDbContext> options) : base(options)
    {
        var services = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException("Configure the global context with UseGeoSurveyControlPlaneSqlServer and approved Identity options.");
        // Validate every instance, including when EF reuses a cached model.
        GlobalControlPlaneOptions.ValidateIdentityOptions(services.GetRequiredService<IOptions<IdentityOptions>>().Value);
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyGeoSurveyIdentityMappings();
        builder.ApplyConfiguration(new OrganizationConfiguration());
        builder.Entity<IdentityAccount>().HasOne<Organization>().WithMany()
            .HasForeignKey(account => account.OrganizationId).IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
