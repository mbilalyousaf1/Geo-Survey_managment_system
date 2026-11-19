using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Tenancy.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

/// <summary>Every request starts with the signed global account, then resolves its current registry and tenant-owned grant.</summary>
public sealed class TenantAccessService(DbContextOptions<GlobalControlPlaneDbContext> options,
    ITenantDatabaseOperations tenantDatabases, TimeProvider clock) : ITenantAdministratorAccess, ITenantSetup
{
    public async Task<bool> CanAccessAsync(Guid accountPublicId, Guid organizationPublicId, CancellationToken cancellationToken = default)
    {
        if (accountPublicId == Guid.Empty || organizationPublicId == Guid.Empty) return false;
        try
        {
            await using var context = PrivateControlPlane.Open(options);
            var now = clock.GetUtcNow();
            var resolved = await (from account in context.Users.AsNoTracking()
                                  join organization in context.Organizations.AsNoTracking() on account.OrganizationId equals organization.Id
                                  join registry in context.Set<TenantRegistryEntry>().AsNoTracking() on organization.Id equals registry.OrganizationId
                                  where account.PublicId == accountPublicId && organization.PublicId == organizationPublicId
                                      && account.AccountScope == AccountScope.Organization && account.PasswordHash != null
                                      && (account.LockoutEnd == null || account.LockoutEnd <= now)
                                      && registry.State == TenantProvisioningState.Ready && registry.SchemaVersion == 1
                                      && registry.DatabaseHealthyAtUtc != null
                                  select new { organization.PublicId, registry.RegionKey, registry.DatabaseName })
                .SingleOrDefaultAsync(cancellationToken);
            if (resolved is null) return false;
            return await tenantDatabases.HasAdministratorGrantAsync(new(resolved.PublicId, resolved.RegionKey, resolved.DatabaseName), accountPublicId, cancellationToken);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or DbUpdateException)
        {
            // Availability failures, invalid registry bindings, and corrupt markers fail closed without diagnostic disclosure.
            return false;
        }
    }

    public async Task<TenantSetupDto?> GetAsync(Guid accountPublicId, CancellationToken cancellationToken = default)
    {
        await using var context = PrivateControlPlane.Open(options);
        var row = await (from account in context.Users.AsNoTracking()
                         join organization in context.Organizations.AsNoTracking() on account.OrganizationId equals organization.Id
                         join registry in context.Set<TenantRegistryEntry>().AsNoTracking() on organization.Id equals registry.OrganizationId
                         where account.PublicId == accountPublicId && account.AccountScope == AccountScope.Organization
                         select new { organization.PublicId, organization.Code, organization.DisplayName, registry.RegionKey, registry.State, registry.SchemaVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || !await CanAccessAsync(accountPublicId, row.PublicId, cancellationToken)) return null;
        return new(row.PublicId, row.Code.Value, row.DisplayName, row.RegionKey, row.State.ToString(), row.SchemaVersion, true, true);
    }
}
