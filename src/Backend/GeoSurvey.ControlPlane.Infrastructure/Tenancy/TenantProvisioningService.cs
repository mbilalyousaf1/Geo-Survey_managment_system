using GeoSurvey.Tenancy.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

/// <summary>Resumable durable job. SQL session locks serialize worker restarts, duplicate outbox delivery, and activation.</summary>
public sealed class TenantProvisioningService(DbContextOptions<GlobalControlPlaneDbContext> options,
    ITenantDatabaseOperations tenantDatabases, TimeProvider clock) : ITenantProvisioning
{
    public const int MaximumAttempts = 4;

    public async Task RunAsync(Guid organizationPublicId, CancellationToken cancellationToken = default)
    {
        if (organizationPublicId == Guid.Empty) return;
        await using var context = PrivateControlPlane.Open(options);
        await using var lease = await PrivateControlPlane.LockAsync(context, organizationPublicId, cancellationToken);
        var organization = await context.Organizations.SingleOrDefaultAsync(row => row.PublicId == organizationPublicId, cancellationToken);
        if (organization is null) return;
        var registry = await context.Set<TenantRegistryEntry>().SingleOrDefaultAsync(row => row.OrganizationId == organization.Id, cancellationToken);
        if (registry is null || registry.State is TenantProvisioningState.Ready or TenantProvisioningState.AwaitingAdministrator or TenantProvisioningState.Suspended
            || registry.ProvisioningAttempts >= MaximumAttempts) return;
        var account = await context.Users.SingleAsync(row => row.Id == registry.InitialAdministratorAccountId, cancellationToken);
        registry.State = TenantProvisioningState.Provisioning;
        registry.ProvisioningAttempts++;
        registry.UpdatedAtUtc = clock.GetUtcNow();
        registry.FailureCode = null;
        context.Add(OrganizationManagementService.Audit(organizationPublicId, null, "provisioning.started", "provisioning", registry.UpdatedAtUtc));
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            var reference = new TrustedTenantReference(organization.PublicId, registry.RegionKey, registry.DatabaseName);
            await tenantDatabases.ProvisionAsync(reference, cancellationToken);
            if (!await tenantDatabases.VerifyHealthAsync(reference, cancellationToken))
                throw new InvalidOperationException("Tenant database health did not pass.");
            var active = account.PasswordHash is not null
                && await tenantDatabases.HasAdministratorGrantAsync(reference, account.PublicId, cancellationToken);
            if (account.PasswordHash is not null && !active)
                throw new InvalidOperationException("The active tenant account does not have its required administrator grant.");
            registry.SchemaVersion = 1;
            registry.DatabaseHealthyAtUtc = clock.GetUtcNow();
            registry.UpdatedAtUtc = registry.DatabaseHealthyAtUtc.Value;
            registry.State = active ? TenantProvisioningState.Ready : TenantProvisioningState.AwaitingAdministrator;
            context.Add(OrganizationManagementService.Audit(organizationPublicId, null, "provisioning.completed", registry.State.ToString(), registry.UpdatedAtUtc));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Preserve Provisioning; Hangfire resumes after restart and revalidates the same database identity.
            throw;
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException or InvalidOperationException)
        {
            registry.State = TenantProvisioningState.Failed;
            registry.FailureCode = "tenant-provisioning-failed";
            registry.UpdatedAtUtc = clock.GetUtcNow();
            context.Add(OrganizationManagementService.Audit(organizationPublicId, null, "provisioning.failed", registry.FailureCode, registry.UpdatedAtUtc));
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (Exception failure) when (failure is SqlException or DbUpdateException)
            {
                throw new InvalidOperationException("Tenant provisioning state could not be saved. The durable job will retry.");
            }
            // Hangfire persists/logs exceptions. Only this fixed message may leave the boundary.
            throw new InvalidOperationException("Tenant provisioning failed. A bounded durable retry may resume it.");
        }
    }
}
