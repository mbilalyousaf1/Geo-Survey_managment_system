using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.ControlPlane.Infrastructure.Tenancy;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Mfa;
using GeoSurvey.Identity.Infrastructure.Persistence;
using GeoSurvey.Identity.Infrastructure.Sessions;
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
    public DbSet<BootstrapCompletion> BootstrapCompletions => Set<BootstrapCompletion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyGeoSurveyIdentityMappings();
        builder.ApplyConfiguration(new OrganizationConfiguration());
        builder.ApplyConfiguration(new BootstrapCompletionConfiguration());
        builder.ApplyConfiguration(new MfaChallengeConfiguration());
        builder.ApplyConfiguration(new PendingTotpEnrollmentConfiguration());
        builder.ApplyConfiguration(new MfaVerificationStateConfiguration());
        builder.ApplyConfiguration(new ConfirmedTotpCredentialConfiguration());
        builder.ApplyConfiguration(new MfaRecoveryCodeConfiguration());
        builder.ApplyConfiguration(new TokenFamilyConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new TenantRegistryEntryConfiguration());
        builder.ApplyConfiguration(new AdministratorInvitationConfiguration());
        builder.ApplyConfiguration(new ProvisioningOutboxMessageConfiguration());
        builder.ApplyConfiguration(new TenantLifecycleAuditConfiguration());
        builder.Entity<IdentityAccount>().HasOne<Organization>().WithMany()
            .HasForeignKey(account => account.OrganizationId).IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
