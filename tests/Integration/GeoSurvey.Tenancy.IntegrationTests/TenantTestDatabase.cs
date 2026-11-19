using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.ControlPlane.Infrastructure.Tenancy;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Persistence;
using GeoSurvey.Organizations.Application;
using GeoSurvey.Tenancy.Application;
using GeoSurvey.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoSurvey.Tenancy.IntegrationTests;

/// <summary>Real disposable SQL databases/logins; no test can target an existing application's tenant database.</summary>
internal sealed class TenantTestDatabase : IAsyncDisposable, IOrganizationRegionProfile
{
    private readonly string _fixtureId = Guid.NewGuid().ToString("N");
    private readonly List<Guid> _organizations = [];
    private readonly ServiceProvider _services = new ServiceCollection().Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
    private string _master = "";
    private bool _globalOwned;
    private bool _loginOwned;
    internal DbContextOptions<GlobalControlPlaneDbContext> Options { get; private set; } = null!;
    internal LocalDemoTenantOptions Profile { get; private set; } = null!;
    internal SqlTenantDatabaseOperations Databases { get; private set; } = null!;
    internal OrganizationManagementService Organizations { get; private set; } = null!;
    internal Guid ActorPublicId { get; private set; }
    public string RegionKey => Profile.RegionKey;
    public string DisplayName => Profile.RegionDisplayName;
    public string DatabaseName(Guid organizationPublicId) => Profile.DatabaseName(organizationPublicId);

    internal static async Task<TenantTestDatabase> CreateAsync()
    {
        var fixture = new TenantTestDatabase();
        try { await fixture.InitializeAsync(); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("GEOSURVEY_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Run the SQL-backed test script with GEOSURVEY_TEST_SQLSERVER. Tenant tests never silently skip SQL.");
        var parsed = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        _master = parsed.ConnectionString;
        var ready = false;
        for (var attempt = 0; attempt < 60 && !ready; attempt++)
        {
            try
            {
                await using var probe = new SqlConnection(_master);
                await probe.OpenAsync();
                ready = true;
            }
            catch (SqlException) { await Task.Delay(TimeSpan.FromSeconds(2)); }
        }
        if (!ready) throw new InvalidOperationException("The isolated test SQL Server did not become ready.");
        var runtimeLogin = "GeoSurveyTenantTest_" + _fixtureId;
        var runtimePassword = "Synthetic!9_" + Guid.NewGuid().ToString("N");
        await using (var master = new SqlConnection(_master))
        {
            await master.OpenAsync();
            await using var createLogin = new SqlCommand($"CREATE LOGIN [{runtimeLogin}] WITH PASSWORD = '{runtimePassword}', CHECK_POLICY=OFF", master);
            await createLogin.ExecuteNonQueryAsync();
            _loginOwned = true;
        }
        var runtime = new SqlConnectionStringBuilder(_master) { UserID = runtimeLogin, Password = runtimePassword, IntegratedSecurity = false };
        Profile = new()
        {
            DatabasePrefix = "GeoSurveyTestTenant_" + _fixtureId + "_",
            RuntimeLogin = runtimeLogin,
            RuntimeConnectionString = runtime.ConnectionString,
            ManagementConnectionString = _master
        };
        parsed.InitialCatalog = "GeoSurvey_TenancyTests_" + _fixtureId;
        Options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>().UseGeoSurveyControlPlaneSqlServer(parsed.ConnectionString, _services).Options;
        await using (var master = new SqlConnection(_master))
        {
            await master.OpenAsync();
            await using var check = new SqlCommand("SELECT DB_ID(@database)", master);
            check.Parameters.AddWithValue("@database", parsed.InitialCatalog);
            if (await check.ExecuteScalarAsync() is not DBNull)
                throw new InvalidOperationException("A disposable global test target unexpectedly already exists; refusing to modify it.");
        }
        await using var global = CreateContext();
        _globalOwned = true;
        await global.Database.MigrateAsync();
        var actor = IdentityAccount.CreatePlatformAccount("operator@example.test");
        actor.TwoFactorEnabled = true;
        actor.SecurityStamp = Guid.NewGuid().ToString("N");
        global.Users.Add(actor);
        await global.SaveChangesAsync();
        global.UserClaims.Add(new() { UserId = actor.Id, ClaimType = PlatformAdministratorClaims.Type, ClaimValue = PlatformAdministratorClaims.Administrator });
        await global.SaveChangesAsync();
        ActorPublicId = actor.PublicId;
        Databases = new(Microsoft.Extensions.Options.Options.Create(Profile), TimeProvider.System);
        Organizations = new(Options, Databases, TimeProvider.System, this);
    }

    internal GlobalControlPlaneDbContext CreateContext() => new(Options);
    internal TenantAccessService Access => new(Options, Databases, TimeProvider.System);
    internal TenantProvisioningService Provisioning => new(Options, Databases, TimeProvider.System);

    internal async Task<OrganizationDto> CreateOrganizationAsync(string code, string email = "administrator@example.test")
    {
        var result = await Organizations.CreateAsync(new(code, "Synthetic " + code, RegionKey, email), ActorPublicId);
        _organizations.Add(result.PublicId);
        return result;
    }

    internal async Task<TrustedTenantReference> ReferenceAsync(Guid publicId)
    {
        await using var global = CreateContext();
        var row = await (from organization in global.Organizations
                         join registry in global.Set<TenantRegistryEntry>() on organization.Id equals registry.OrganizationId
                         where organization.PublicId == publicId
                         select registry).SingleAsync();
        return new(publicId, row.RegionKey, row.DatabaseName);
    }

    internal async Task<IdentityAccount> AdministratorAsync(Guid publicId)
    {
        await using var global = CreateContext();
        return await (from organization in global.Organizations
                      join registry in global.Set<TenantRegistryEntry>() on organization.Id equals registry.OrganizationId
                      join account in global.Users on registry.InitialAdministratorAccountId equals account.Id
                      where organization.PublicId == publicId
                      select account).AsNoTracking().SingleAsync();
    }

    internal TenantDbContext TenantContext(TrustedTenantReference tenant, bool management = false) => new(new DbContextOptionsBuilder<TenantDbContext>()
        .UseSqlServer(Profile.ConnectionFor(tenant, management), sql => sql.MigrationsHistoryTable("__TenantMigrations", "tenant")).Options);

    internal TrustedTenantReference OwnedPermissionProbe()
    {
        var publicId = Guid.NewGuid();
        _organizations.Add(publicId); // Cleanup also covers a permission regression that unexpectedly creates this probe database.
        return new(publicId, Profile.RegionKey, Profile.DatabaseName(publicId));
    }

    public async ValueTask DisposeAsync()
    {
        // Every target is generated inside this fixture; preserve anything outside that explicit owned set.
        if (Profile is not null)
        {
            foreach (var organization in _organizations)
            {
                var reference = new TrustedTenantReference(organization, Profile.RegionKey, Profile.DatabaseName(organization));
                await using var tenant = TenantContext(reference, management: true);
                await tenant.Database.EnsureDeletedAsync();
            }
        }
        if (_globalOwned)
        {
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
        if (_loginOwned)
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(_master);
            await master.OpenAsync();
            await using var drop = new SqlCommand($"DROP LOGIN [GeoSurveyTenantTest_{_fixtureId}]", master);
            await drop.ExecuteNonQueryAsync();
        }
        await _services.DisposeAsync();
    }
}
