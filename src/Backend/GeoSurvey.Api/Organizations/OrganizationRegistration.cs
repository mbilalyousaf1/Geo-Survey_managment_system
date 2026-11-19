using GeoSurvey.ControlPlane.Infrastructure.Tenancy;
using GeoSurvey.Organizations.Application;
using GeoSurvey.Tenancy.Application;
using GeoSurvey.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Api.Organizations;

public static class OrganizationRegistration
{
    public static IServiceCollection AddPhaseOneOrganizations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalDemoTenantOptions>().Bind(configuration.GetSection(LocalDemoTenantOptions.SectionName))
            .Validate(options => options.IsValid(), "Configure a valid trusted local-demo tenant runtime profile.").ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IOrganizationRegionProfile, LocalDemoRegionProfile>();
        services.AddScoped<ITenantDatabaseOperations, SqlTenantDatabaseOperations>();
        services.AddScoped<IOrganizationManagement, OrganizationManagementService>();
        services.AddScoped<TenantAccessService>();
        services.AddScoped<ITenantAdministratorAccess>(provider => provider.GetRequiredService<TenantAccessService>());
        services.AddScoped<ITenantSetup>(provider => provider.GetRequiredService<TenantAccessService>());
        return services;
    }

    private sealed class LocalDemoRegionProfile(IOptions<LocalDemoTenantOptions> options) : IOrganizationRegionProfile
    {
        public string RegionKey => options.Value.RegionKey;
        public string DisplayName => options.Value.RegionDisplayName;
        public string DatabaseName(Guid organizationPublicId) => options.Value.DatabaseName(organizationPublicId);
    }
}
