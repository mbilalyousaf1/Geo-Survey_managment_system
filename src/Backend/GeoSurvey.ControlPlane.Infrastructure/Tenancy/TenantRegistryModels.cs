namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

public enum TenantProvisioningState
{
    Pending,
    Provisioning,
    AwaitingAdministrator,
    Ready,
    Failed,
    Suspended
}

public sealed class TenantRegistryEntry
{
    public int OrganizationId { get; set; }
    public string RegionKey { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public TenantProvisioningState State { get; set; }
    public int InitialAdministratorAccountId { get; set; }
    public int ProvisioningAttempts { get; set; }
    public int SchemaVersion { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DatabaseHealthyAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AdministratorInvitation
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int OrganizationId { get; set; }
    public int AccountId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class ProvisioningOutboxMessage
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int OrganizationId { get; set; }
    public Guid OrganizationPublicId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DispatchedAtUtc { get; set; }
    public string? JobId { get; set; }
}

/// <summary>Append-only operational/security events; no credential or database target fields.</summary>
public sealed class TenantLifecycleAudit
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid OrganizationPublicId { get; set; }
    public Guid? ActorPublicId { get; set; }
    public string EventType { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
}
