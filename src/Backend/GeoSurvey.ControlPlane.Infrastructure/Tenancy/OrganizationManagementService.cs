using System.Data;
using System.Security.Cryptography;
using System.Text;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Application;
using GeoSurvey.Organizations.Domain;
using GeoSurvey.Tenancy.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

/// <summary>Global organization registry and invitation lifecycle; tenant writes use only the explicit tenancy contract.</summary>
public sealed class OrganizationManagementService(DbContextOptions<GlobalControlPlaneDbContext> options,
    ITenantDatabaseOperations tenantDatabases, TimeProvider clock, IOrganizationRegionProfile profile) : IOrganizationManagement
{
    public IReadOnlyList<RegionDto> GetRegions() => [new(profile.RegionKey, profile.DisplayName)];

    public async Task<OrganizationPageDto> ListAsync(int pageNumber = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100 || pageNumber > 1000000)
            throw new OrganizationOperationException("invalid-pagination", "Page number must be positive and page size must be 1-100.", 400);
        await using var context = PrivateControlPlane.Open(options);
        var query = from organization in context.Organizations.AsNoTracking()
                    join registry in context.Set<TenantRegistryEntry>().AsNoTracking() on organization.Id equals registry.OrganizationId
                    join account in context.Users.AsNoTracking() on registry.InitialAdministratorAccountId equals account.Id
                    orderby organization.CreatedAtUtc descending, organization.Id descending
                    select new { organization, registry, email = account.Email! };
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new(rows.Select(row => Dto(row.organization, row.registry, row.email)).ToArray(), count, pageNumber, pageSize);
    }

    public async Task<OrganizationDto?> GetAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        await using var context = PrivateControlPlane.Open(options);
        return await ReadDtoAsync(context, publicId, cancellationToken);
    }

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, Guid actorPublicId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Organization organization;
        AccountEmail email;
        try
        {
            organization = Organization.Create(OrganizationCode.Create(request.Code), request.DisplayName);
            email = AccountEmail.Create(request.AdministratorEmail);
        }
        catch (ArgumentException)
        {
            throw new OrganizationOperationException("invalid-organization", "Supply a valid organization code, display name, and administrator email.", 400);
        }
        if (request.RegionKey != profile.RegionKey)
            throw new OrganizationOperationException("invalid-region", "Select one of the server's available regions.", 400);

        try
        {
            await using var context = PrivateControlPlane.Open(options);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await RequirePlatformAdministratorAsync(context, actorPublicId, cancellationToken);
            if (await context.Organizations.AnyAsync(row => row.Code == organization.Code, cancellationToken))
                throw new OrganizationOperationException("organization-code-unavailable", "That organization code is already in use.");
            context.Organizations.Add(organization);
            await context.SaveChangesAsync(cancellationToken);
            var account = IdentityAccount.CreateOrganizationAccount(organization.Id, email.DisplayValue);
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            account.LockoutEnabled = true;
            // No password exists before the tenant's durable initial-administrator grant is committed.
            context.Users.Add(account);
            await context.SaveChangesAsync(cancellationToken);
            var now = UtcNow();
            var registry = new TenantRegistryEntry
            {
                OrganizationId = organization.Id,
                RegionKey = profile.RegionKey,
                DatabaseName = profile.DatabaseName(organization.PublicId),
                State = TenantProvisioningState.Pending,
                InitialAdministratorAccountId = account.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.Add(registry);
            context.Add(NewOutbox(organization, now));
            context.Add(Audit(organization.PublicId, actorPublicId, "organization.created", "pending", now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Dto(organization, registry, account.Email!);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new OrganizationOperationException("organization-code-unavailable", "That organization code is already in use.");
        }
        catch (Exception exception) when (exception is SqlException or DbUpdateException)
        {
            throw new OrganizationOperationException("organization-write-unavailable", "The organization could not be saved. Retry shortly.", 503);
        }
    }

    public async Task<OrganizationDto> RetryAsync(Guid publicId, Guid actorPublicId, CancellationToken cancellationToken = default)
    {
        await using var context = PrivateControlPlane.Open(options);
        await using var lease = await PrivateControlPlane.LockAsync(context, publicId, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await RequirePlatformAdministratorAsync(context, actorPublicId, cancellationToken);
        var (organization, registry, account) = await RequiredAsync(context, publicId, cancellationToken);
        if (registry.State != TenantProvisioningState.Failed)
            throw new OrganizationOperationException("provisioning-retry-unavailable", "Only a failed organization can be retried.");
        var now = UtcNow();
        registry.State = TenantProvisioningState.Pending;
        registry.FailureCode = null;
        registry.ProvisioningAttempts = 0;
        registry.UpdatedAtUtc = now;
        context.Add(NewOutbox(organization, now));
        context.Add(Audit(publicId, actorPublicId, "provisioning.retry-requested", "pending", now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Dto(organization, registry, account.Email!);
    }

    public async Task<AdministratorInvitationDto> IssueInvitationAsync(Guid publicId, Guid actorPublicId, CancellationToken cancellationToken = default)
    {
        await using var context = PrivateControlPlane.Open(options);
        await using var lease = await PrivateControlPlane.LockAsync(context, publicId, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await RequirePlatformAdministratorAsync(context, actorPublicId, cancellationToken);
        var (organization, registry, account) = await RequiredAsync(context, publicId, cancellationToken);
        if (registry.State != TenantProvisioningState.AwaitingAdministrator || account.PasswordHash is not null)
            throw new OrganizationOperationException("invitation-unavailable", "Invitation is available only for an unactivated administrator after provisioning.");
        var now = UtcNow();
        await context.Set<AdministratorInvitation>().Where(row => row.AccountId == account.Id && row.ConsumedAtUtc == null && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtUtc, (DateTimeOffset?)now), cancellationToken);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = now.AddHours(24);
        context.Add(new AdministratorInvitation
        {
            PublicId = Guid.NewGuid(),
            AccountId = account.Id,
            OrganizationId = organization.Id,
            TokenHash = HashToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = expires
        });
        context.Add(Audit(publicId, actorPublicId, "administrator.invitation-issued", "issued", now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(token, expires, organization.Code.Value, account.Email!);
    }

    public async Task<ActivationInspectionDto?> InspectActivationAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!IsToken(token)) return null;
        await using var context = PrivateControlPlane.Open(options);
        var hash = HashToken(token!);
        var now = UtcNow();
        var invitation = await ValidInvitations(context, hash, now).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (invitation is null) return null;
        var organization = await context.Organizations.AsNoTracking().SingleAsync(row => row.Id == invitation.OrganizationId, cancellationToken);
        var account = await context.Users.AsNoTracking().SingleAsync(row => row.Id == invitation.AccountId, cancellationToken);
        return new(organization.Code.Value, organization.DisplayName, account.Email!, invitation.ExpiresAtUtc);
    }

    public async Task<ActivationCompletionDto?> CompleteActivationAsync(string? token, string? password, CancellationToken cancellationToken = default)
    {
        if (!IsToken(token)) return null;
        string normalized;
        try { normalized = LocalPasswordBlocklist.LoadPackaged().NormalizeAndValidate(password!); }
        catch (ArgumentException)
        {
            throw new OrganizationOperationException("invalid-password", "Choose a password of 15-128 Unicode characters that is absent from the local common-password blocklist.", 400);
        }
        await using var context = PrivateControlPlane.Open(options);
        var hash = HashToken(token!);
        var initial = await ValidInvitations(context, hash, UtcNow()).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (initial is null) return null;
        var organizationPublicId = await context.Organizations.Where(row => row.Id == initial.OrganizationId).Select(row => row.PublicId).SingleAsync(cancellationToken);
        await using var lease = await PrivateControlPlane.LockAsync(context, organizationPublicId, cancellationToken);
        var invitation = await ValidInvitations(context, hash, UtcNow()).SingleOrDefaultAsync(cancellationToken);
        if (invitation is null) return null;
        var (organization, registry, account) = await RequiredAsync(context, organizationPublicId, cancellationToken);
        var reference = new TrustedTenantReference(organization.PublicId, registry.RegionKey, registry.DatabaseName);
        if (!await tenantDatabases.VerifyHealthAsync(reference, cancellationToken))
            throw new OrganizationOperationException("tenant-unavailable", "The organization database is not ready. Ask the platform administrator to check its status.", 503);
        // A committed tenant grant precedes any usable global credential. A later global failure leaves a passwordless account.
        await tenantDatabases.EnsureAdministratorGrantAsync(reference, account.PublicId, cancellationToken);
        if (!await tenantDatabases.HasAdministratorGrantAsync(reference, account.PublicId, cancellationToken))
            throw new OrganizationOperationException("administrator-grant-unavailable", "Administrator activation could not be completed. Retry shortly.", 503);

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [identity].[Accounts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {account.Id}", cancellationToken);
        await context.Entry(account).ReloadAsync(cancellationToken);
        await context.Entry(invitation).ReloadAsync(cancellationToken);
        await context.Entry(registry).ReloadAsync(cancellationToken);
        var completedAt = UtcNow();
        if (account.PasswordHash is not null || account.OrganizationId != organization.Id || account.AccountScope != AccountScope.Organization
            || invitation.ConsumedAtUtc is not null || invitation.RevokedAtUtc is not null || invitation.ExpiresAtUtc <= completedAt
            || registry.State != TenantProvisioningState.AwaitingAdministrator || registry.InitialAdministratorAccountId != account.Id)
            return null;
        var hasherOptions = new PasswordHasherOptions();
        IdentityPasswordHashingOptions.Configure(hasherOptions);
        account.PasswordHash = new PasswordHasher<IdentityAccount>(Options.Create(hasherOptions)).HashPassword(account, normalized);
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        invitation.ConsumedAtUtc = completedAt;
        registry.State = TenantProvisioningState.Ready;
        registry.UpdatedAtUtc = completedAt;
        registry.FailureCode = null;
        context.Add(Audit(organization.PublicId, account.PublicId, "administrator.activated", "ready", completedAt));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(organization.Code.Value, account.Email!);
    }

    private static IQueryable<AdministratorInvitation> ValidInvitations(GlobalControlPlaneDbContext context, byte[] hash, DateTimeOffset now) =>
        context.Set<AdministratorInvitation>().Where(row => row.TokenHash == hash && row.ConsumedAtUtc == null && row.RevokedAtUtc == null && row.ExpiresAtUtc > now
            && context.Set<TenantRegistryEntry>().Any(registry => registry.OrganizationId == row.OrganizationId
                && registry.InitialAdministratorAccountId == row.AccountId && registry.State == TenantProvisioningState.AwaitingAdministrator)
            && context.Users.Any(account => account.Id == row.AccountId && account.OrganizationId == row.OrganizationId
                && account.AccountScope == AccountScope.Organization && account.PasswordHash == null));

    private static async Task RequirePlatformAdministratorAsync(GlobalControlPlaneDbContext context, Guid actor, CancellationToken ct)
    {
        if (!await context.Users.AnyAsync(account => account.PublicId == actor && account.AccountScope == AccountScope.Platform && account.TwoFactorEnabled
            && context.UserClaims.Any(claim => claim.UserId == account.Id && claim.ClaimType == PlatformAdministratorClaims.Type
                && claim.ClaimValue == PlatformAdministratorClaims.Administrator), ct))
            throw new OrganizationOperationException("platform-administrator-required", "Platform administrator access is required.", 403);
    }

    private static async Task<(Organization, TenantRegistryEntry, IdentityAccount)> RequiredAsync(GlobalControlPlaneDbContext context, Guid publicId, CancellationToken ct)
    {
        var organization = await context.Organizations.SingleOrDefaultAsync(row => row.PublicId == publicId, ct)
            ?? throw new OrganizationOperationException("organization-not-found", "Organization was not found.", 404);
        var registry = await context.Set<TenantRegistryEntry>().SingleOrDefaultAsync(row => row.OrganizationId == organization.Id, ct)
            ?? throw new OrganizationOperationException("registry-unavailable", "The organization registry is unavailable.", 503);
        var account = await context.Users.SingleAsync(row => row.Id == registry.InitialAdministratorAccountId, ct);
        if (account.OrganizationId != organization.Id || account.AccountScope != AccountScope.Organization)
            throw new OrganizationOperationException("registry-unavailable", "The organization registry is unavailable.", 503);
        return (organization, registry, account);
    }

    private static async Task<OrganizationDto?> ReadDtoAsync(GlobalControlPlaneDbContext context, Guid publicId, CancellationToken ct)
    {
        if (!await context.Organizations.AnyAsync(row => row.PublicId == publicId, ct)) return null;
        var (organization, registry, account) = await RequiredAsync(context, publicId, ct);
        return Dto(organization, registry, account.Email!);
    }

    private DateTimeOffset UtcNow()
    {
        var now = clock.GetUtcNow();
        if (now.Offset != TimeSpan.Zero) throw new InvalidOperationException("The organization clock must supply UTC.");
        return now;
    }

    internal static TenantLifecycleAudit Audit(Guid organization, Guid? actor, string type, string outcome, DateTimeOffset now) => new()
    {
        PublicId = Guid.NewGuid(),
        OrganizationPublicId = organization,
        ActorPublicId = actor,
        EventType = type,
        Outcome = outcome,
        OccurredAtUtc = now
    };
    private static ProvisioningOutboxMessage NewOutbox(Organization organization, DateTimeOffset now) => new()
    {
        PublicId = Guid.NewGuid(),
        OrganizationId = organization.Id,
        OrganizationPublicId = organization.PublicId,
        CreatedAtUtc = now
    };
    private static OrganizationDto Dto(Organization org, TenantRegistryEntry registry, string email) =>
        new(org.PublicId, org.Code.Value, org.DisplayName, registry.RegionKey, registry.State.ToString(), email, registry.CreatedAtUtc, registry.UpdatedAtUtc, registry.FailureCode);
    private static bool IsToken(string? token) => token is { Length: 64 } && token.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));
}

/// <summary>Server configuration adapter avoids coupling global persistence to the tenancy infrastructure project.</summary>
public interface IOrganizationRegionProfile
{
    string RegionKey { get; }
    string DisplayName { get; }
    string DatabaseName(Guid organizationPublicId);
}
