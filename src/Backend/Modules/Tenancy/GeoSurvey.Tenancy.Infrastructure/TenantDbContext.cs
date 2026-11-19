using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoSurvey.Tenancy.Infrastructure;

public sealed class TenantIdentity
{
    public int Id { get; private set; } = 1;
    public Guid OrganizationPublicId { get; private set; }
    public int SchemaVersion { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private TenantIdentity() { }
    internal TenantIdentity(Guid organizationPublicId, DateTimeOffset now)
    {
        OrganizationPublicId = organizationPublicId;
        CreatedAtUtc = now;
    }
}

public sealed class TenantAdministratorGrant
{
    public Guid AccountPublicId { get; private set; }
    public DateTimeOffset GrantedAtUtc { get; private set; }
    private TenantAdministratorGrant() { }
    internal TenantAdministratorGrant(Guid accountPublicId, DateTimeOffset now)
    {
        AccountPublicId = accountPublicId;
        GrantedAtUtc = now;
    }
}

/// <summary>A fresh instance is created for exactly one trusted organization; never pooled across tenants.</summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : DbContext(options)
{
    public DbSet<TenantIdentity> Identity => Set<TenantIdentity>();
    public DbSet<TenantAdministratorGrant> AdministratorGrants => Set<TenantAdministratorGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var identity = modelBuilder.Entity<TenantIdentity>();
        identity.ToTable("OrganizationIdentity", "tenant", table =>
        {
            table.HasCheckConstraint("CK_OrganizationIdentity_Singleton", "[Id] = 1");
            table.HasCheckConstraint("CK_OrganizationIdentity_SchemaVersion", "[SchemaVersion] >= 1");
        });
        identity.HasKey(row => row.Id);
        identity.Property(row => row.Id).ValueGeneratedNever();
        identity.HasIndex(row => row.OrganizationPublicId).IsUnique();
        identity.Property(row => row.OrganizationPublicId).ValueGeneratedNever();
        identity.Property(row => row.CreatedAtUtc).HasPrecision(7);
        var grant = modelBuilder.Entity<TenantAdministratorGrant>();
        grant.ToTable("AdministratorGrants", "tenant");
        grant.HasKey(row => row.AccountPublicId);
        grant.Property(row => row.AccountPublicId).ValueGeneratedNever();
        grant.Property(row => row.GrantedAtUtc).HasPrecision(7);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureImmutableIdentity();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureImmutableIdentity();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureImmutableIdentity()
    {
        if (ChangeTracker.Entries<TenantIdentity>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("A tenant database identity is immutable.");
    }
}

public sealed class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        if (args.Length != 0) throw new ArgumentException("Design-time arguments are not supported.", nameof(args));
        var connection = Environment.GetEnvironmentVariable("GEOSURVEY_DESIGNTIME_TENANT_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set GEOSURVEY_DESIGNTIME_TENANT_SQLSERVER for tenant migration tooling.");
        return new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__TenantMigrations", "tenant")).Options);
    }
}
