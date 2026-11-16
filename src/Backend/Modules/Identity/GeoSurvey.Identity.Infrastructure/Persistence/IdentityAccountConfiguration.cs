using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

public sealed class IdentityAccountConfiguration : IEntityTypeConfiguration<IdentityAccount>
{
    public void Configure(EntityTypeBuilder<IdentityAccount> builder)
    {
        builder.ToTable("Accounts", "identity", table => table.HasCheckConstraint(
            "CK_Accounts_ScopeOrganization",
            "([AccountScope] = 1 AND [OrganizationId] IS NULL) OR ([AccountScope] = 2 AND [OrganizationId] IS NOT NULL AND [OrganizationId] > 0)"));
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedOnAdd();
        builder.Property(account => account.PublicId)
            .HasColumnType("uniqueidentifier").ValueGeneratedNever().IsRequired();
        builder.HasIndex(account => account.PublicId).HasDatabaseName("UX_Accounts_PublicId").IsUnique();
        builder.Property(account => account.AccountScope).HasConversion<int>().IsRequired();
        // The cross-module Organization FK belongs to ControlPlane composition, not this module.
        builder.Property(account => account.OrganizationId).IsRequired(false);
        builder.Property(account => account.Email).HasMaxLength(254).IsRequired();
        builder.Property(account => account.NormalizedEmail)
            .HasMaxLength(254).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(account => account.UserName).HasMaxLength(32).IsRequired();
        builder.Property(account => account.NormalizedUserName)
            .HasMaxLength(32).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(account => account.ConcurrencyStamp).IsConcurrencyToken();

        // Reconfigure Identity's existing indexes on these properties rather than duplicating them.
        builder.HasIndex(account => account.NormalizedUserName)
            .HasDatabaseName("UX_Accounts_NormalizedUserName").IsUnique().HasFilter(null);
        builder.HasIndex(account => account.NormalizedEmail)
            .HasDatabaseName("UX_Accounts_PlatformEmail").IsUnique().HasFilter("[AccountScope] = 1");
        builder.HasIndex(account => new { account.OrganizationId, account.NormalizedEmail })
            .HasDatabaseName("UX_Accounts_OrganizationEmail").IsUnique().HasFilter("[AccountScope] = 2");
    }
}
