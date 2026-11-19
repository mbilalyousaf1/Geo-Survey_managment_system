using System.Data;
using GeoSurvey.Tenancy.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Tenancy.Infrastructure;

/// <summary>Only ProvisionAsync uses the worker's privileged connection. Runtime methods cannot create databases.</summary>
public sealed class SqlTenantDatabaseOperations(IOptions<LocalDemoTenantOptions> configured, TimeProvider clock) : ITenantDatabaseOperations
{
    private LocalDemoTenantOptions Profile => configured.Value;

    public async Task ProvisionAsync(TrustedTenantReference tenant, CancellationToken cancellationToken = default)
    {
        var managementTarget = Profile.ConnectionFor(tenant, management: true);
        var masterTarget = new SqlConnectionStringBuilder(managementTarget) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterTarget);
        await master.OpenAsync(cancellationToken);
        await using var lockCommand = new SqlCommand("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000; SELECT @result;", master);
        lockCommand.Parameters.AddWithValue("@resource", "GeoSurveyTenant:" + tenant.OrganizationPublicId.ToString("N"));
        if (Convert.ToInt32(await lockCommand.ExecuteScalarAsync(cancellationToken)) < 0)
            throw new InvalidOperationException("The tenant provisioning lock could not be obtained.");
        try
        {
            await using var existsCommand = new SqlCommand("SELECT DB_ID(@database)", master);
            existsCommand.Parameters.AddWithValue("@database", tenant.DatabaseName);
            var exists = await existsCommand.ExecuteScalarAsync(cancellationToken) is not DBNull;
            if (!exists)
            {
                // This identifier is derived from a public GUID and a validated server-owned prefix, never HTTP input.
                await using var create = new SqlCommand($"CREATE DATABASE [{tenant.DatabaseName}]", master) { CommandTimeout = 120 };
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var target = new SqlConnection(managementTarget);
            await target.OpenAsync(cancellationToken);
            var owner = await ReadOwnershipAsync(target, cancellationToken);
            if (owner is null && !exists)
            {
                await using var bind = new SqlCommand("EXEC sys.sp_addextendedproperty @name=N'GeoSurvey.OrganizationPublicId', @value=@organization;", target);
                bind.Parameters.AddWithValue("@organization", tenant.OrganizationPublicId.ToString("D"));
                await bind.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (!Guid.TryParse(owner, out var ownerId) || ownerId != tenant.OrganizationPublicId)
                throw new InvalidOperationException("The existing database does not have the expected tenant ownership marker.");

            await using var context = CreateContext(managementTarget);
            await context.Database.MigrateAsync(cancellationToken);
            var identity = await context.Identity.SingleOrDefaultAsync(cancellationToken);
            if (identity is null)
            {
                context.Identity.Add(new TenantIdentity(tenant.OrganizationPublicId, clock.GetUtcNow()));
                await context.SaveChangesAsync(cancellationToken);
            }
            else if (identity.OrganizationPublicId != tenant.OrganizationPublicId || identity.SchemaVersion != 1)
                throw new InvalidOperationException("The tenant database identity does not match its trusted registry.");

            // Operator configured identifiers are strictly limited before interpolation; public routes contain no SQL targets.
            await using var permissions = new SqlCommand($"""
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
                    CREATE USER [{Profile.RuntimeLogin}] FOR LOGIN [{Profile.RuntimeLogin}];
                GRANT SELECT ON [tenant].[OrganizationIdentity] TO [{Profile.RuntimeLogin}];
                DENY INSERT, UPDATE, DELETE ON [tenant].[OrganizationIdentity] TO [{Profile.RuntimeLogin}];
                GRANT SELECT, INSERT ON [tenant].[AdministratorGrants] TO [{Profile.RuntimeLogin}];
                DENY UPDATE, DELETE ON [tenant].[AdministratorGrants] TO [{Profile.RuntimeLogin}];
                """, target);
            permissions.Parameters.AddWithValue("@login", Profile.RuntimeLogin);
            await permissions.ExecuteNonQueryAsync(cancellationToken);
            if (!await VerifyHealthAsync(tenant, cancellationToken))
                throw new InvalidOperationException("The tenant database health check did not pass.");
        }
        finally
        {
            await using var release = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';", master);
            release.Parameters.AddWithValue("@resource", "GeoSurveyTenant:" + tenant.OrganizationPublicId.ToString("N"));
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public async Task<bool> VerifyHealthAsync(TrustedTenantReference tenant, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext(Profile.ConnectionFor(tenant));
        var identities = await context.Identity.AsNoTracking().Take(2).ToListAsync(cancellationToken);
        return identities.Count == 1 && identities[0].OrganizationPublicId == tenant.OrganizationPublicId
            && identities[0].SchemaVersion == 1;
    }

    public async Task EnsureAdministratorGrantAsync(TrustedTenantReference tenant, Guid accountPublicId, CancellationToken cancellationToken = default)
    {
        if (accountPublicId == Guid.Empty) throw new ArgumentException("An account public identifier is required.", nameof(accountPublicId));
        await using var context = CreateContext(Profile.ConnectionFor(tenant));
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IdentityMatchesAsync(context, tenant, cancellationToken))
            throw new InvalidOperationException("The tenant database identity does not match its trusted registry.");
        if (!await context.AdministratorGrants.AnyAsync(row => row.AccountPublicId == accountPublicId, cancellationToken))
        {
            context.AdministratorGrants.Add(new TenantAdministratorGrant(accountPublicId, clock.GetUtcNow()));
            await context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasAdministratorGrantAsync(TrustedTenantReference tenant, Guid accountPublicId, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext(Profile.ConnectionFor(tenant));
        return await IdentityMatchesAsync(context, tenant, cancellationToken)
            && await context.AdministratorGrants.AsNoTracking().AnyAsync(row => row.AccountPublicId == accountPublicId, cancellationToken);
    }

    private static async Task<bool> IdentityMatchesAsync(TenantDbContext context, TrustedTenantReference tenant, CancellationToken ct)
    {
        var identity = await context.Identity.AsNoTracking().SingleOrDefaultAsync(ct);
        return identity?.OrganizationPublicId == tenant.OrganizationPublicId && identity.SchemaVersion == 1;
    }

    private static TenantDbContext CreateContext(string connection) => new(new DbContextOptionsBuilder<TenantDbContext>()
        .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__TenantMigrations", "tenant"))
        .UseLoggerFactory(NullLoggerFactory.Instance).EnableSensitiveDataLogging(false).Options);

    private static async Task<string?> ReadOwnershipAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(36), [value]) FROM sys.extended_properties WHERE [class] = 0 AND [name] = N'GeoSurvey.OrganizationPublicId'", connection);
        return await command.ExecuteScalarAsync(ct) as string;
    }
}
