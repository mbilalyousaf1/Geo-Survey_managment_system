using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Organizations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.ControlPlane.Infrastructure.Tenancy;

public sealed class TenantRegistryEntryConfiguration : IEntityTypeConfiguration<TenantRegistryEntry>
{
    public void Configure(EntityTypeBuilder<TenantRegistryEntry> builder)
    {
        builder.ToTable("TenantRegistry", "tenancy", table =>
        {
            table.HasCheckConstraint("CK_TenantRegistry_State", "[State] IN (N'Pending',N'Provisioning',N'AwaitingAdministrator',N'Ready',N'Failed',N'Suspended')");
            table.HasCheckConstraint("CK_TenantRegistry_Attempts", "[ProvisioningAttempts] >= 0");
            table.HasCheckConstraint("CK_TenantRegistry_Ready", "[State] NOT IN (N'Ready',N'AwaitingAdministrator') OR ([SchemaVersion] = 1 AND [DatabaseHealthyAtUtc] IS NOT NULL)");
        });
        builder.HasKey(row => row.OrganizationId);
        builder.Property(row => row.OrganizationId).ValueGeneratedNever();
        builder.Property(row => row.RegionKey).HasMaxLength(50).IsRequired();
        builder.Property(row => row.DatabaseName).HasMaxLength(128).IsRequired();
        builder.HasIndex(row => row.DatabaseName).IsUnique();
        builder.Property(row => row.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.FailureCode).HasMaxLength(80);
        builder.Property(row => row.CreatedAtUtc).HasPrecision(7);
        builder.Property(row => row.UpdatedAtUtc).HasPrecision(7);
        builder.Property(row => row.DatabaseHealthyAtUtc).HasPrecision(7);
        builder.Property(row => row.RowVersion).IsRowVersion();
        builder.HasOne<Organization>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(row => row.InitialAdministratorAccountId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AdministratorInvitationConfiguration : IEntityTypeConfiguration<AdministratorInvitation>
{
    public void Configure(EntityTypeBuilder<AdministratorInvitation> builder)
    {
        builder.ToTable("AdministratorInvitations", "tenancy", table =>
        {
            table.HasCheckConstraint("CK_AdministratorInvitations_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.HasCheckConstraint("CK_AdministratorInvitations_Outcome", "[ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.PublicId).ValueGeneratedNever();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.Property(row => row.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(row => row.TokenHash).IsUnique();
        builder.HasIndex(row => row.AccountId).IsUnique().HasFilter("[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");
        builder.Property(row => row.CreatedAtUtc).HasPrecision(7);
        builder.Property(row => row.ExpiresAtUtc).HasPrecision(7);
        builder.Property(row => row.ConsumedAtUtc).HasPrecision(7);
        builder.Property(row => row.RevokedAtUtc).HasPrecision(7);
        builder.HasOne<Organization>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(row => row.AccountId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ProvisioningOutboxMessageConfiguration : IEntityTypeConfiguration<ProvisioningOutboxMessage>
{
    public void Configure(EntityTypeBuilder<ProvisioningOutboxMessage> builder)
    {
        builder.ToTable("ProvisioningOutbox", "tenancy");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.PublicId).ValueGeneratedNever();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => row.DispatchedAtUtc);
        builder.Property(row => row.OrganizationPublicId).ValueGeneratedNever();
        builder.Property(row => row.CreatedAtUtc).HasPrecision(7);
        builder.Property(row => row.DispatchedAtUtc).HasPrecision(7);
        builder.Property(row => row.JobId).HasMaxLength(100);
        builder.HasOne<Organization>().WithMany().HasForeignKey(row => row.OrganizationId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TenantLifecycleAuditConfiguration : IEntityTypeConfiguration<TenantLifecycleAudit>
{
    public void Configure(EntityTypeBuilder<TenantLifecycleAudit> builder)
    {
        builder.ToTable("TenantLifecycleAudit", "audit");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.PublicId).ValueGeneratedNever();
        builder.HasIndex(row => row.PublicId).IsUnique();
        builder.HasIndex(row => new { row.OrganizationPublicId, row.OccurredAtUtc });
        builder.Property(row => row.EventType).HasMaxLength(80).IsRequired();
        builder.Property(row => row.Outcome).HasMaxLength(40).IsRequired();
        builder.Property(row => row.OccurredAtUtc).HasPrecision(7);
    }
}
