namespace GeoSurvey.Tenancy.Application;

public interface ITenantAdministratorAccess
{
    Task<bool> CanAccessAsync(Guid accountPublicId, Guid organizationPublicId, CancellationToken cancellationToken = default);
}

public sealed record TenantSetupDto(Guid OrganizationPublicId, string OrganizationCode, string OrganizationName,
    string RegionKey, string State, int SchemaVersion, bool DatabaseIdentityVerified, bool AdministratorAccess);

public interface ITenantSetup
{
    Task<TenantSetupDto?> GetAsync(Guid accountPublicId, CancellationToken cancellationToken = default);
}

/// <summary>Internal server contract only. The registry owns the database reference; HTTP input cannot populate it.</summary>
public sealed record TrustedTenantReference(Guid OrganizationPublicId, string RegionKey, string DatabaseName);

public interface ITenantDatabaseOperations
{
    Task ProvisionAsync(TrustedTenantReference tenant, CancellationToken cancellationToken = default);
    Task<bool> VerifyHealthAsync(TrustedTenantReference tenant, CancellationToken cancellationToken = default);
    Task EnsureAdministratorGrantAsync(TrustedTenantReference tenant, Guid accountPublicId, CancellationToken cancellationToken = default);
    Task<bool> HasAdministratorGrantAsync(TrustedTenantReference tenant, Guid accountPublicId, CancellationToken cancellationToken = default);
}

/// <summary>The durable job contains only a public organization reference, never a target or a credential.</summary>
public interface ITenantProvisioning
{
    Task RunAsync(Guid organizationPublicId, CancellationToken cancellationToken = default);
}

public interface IProvisioningJobQueue
{
    string Enqueue(Guid organizationPublicId);
}

public interface IProvisioningOutbox
{
    Task<int> DispatchAsync(IProvisioningJobQueue queue, CancellationToken cancellationToken = default);
}
