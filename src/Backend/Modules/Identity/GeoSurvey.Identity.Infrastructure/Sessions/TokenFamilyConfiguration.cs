using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Sessions;

public sealed class TokenFamilyConfiguration : IEntityTypeConfiguration<TokenFamily>
{
    public void Configure(EntityTypeBuilder<TokenFamily> builder)
    {
        builder.ToTable("TokenFamilies", "identity", table =>
        {
            table.HasCheckConstraint("CK_TokenFamilies_PublicId", "[PublicId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_TokenFamilies_SecurityStamp", "LEN([SecurityStamp]) > 0");
            table.HasCheckConstraint("CK_TokenFamilies_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.HasCheckConstraint("CK_TokenFamilies_RevokedAt", "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]");
            table.HasCheckConstraint("CK_TokenFamilies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([RevokedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
        });
        builder.HasKey(family => family.Id);
        builder.Property(family => family.Id).ValueGeneratedOnAdd();
        builder.Property(family => family.PublicId).ValueGeneratedNever();
        builder.Property(family => family.SecurityStamp).IsRequired();
        builder.Property(family => family.CreatedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(family => family.ExpiresAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(family => family.RevokedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasIndex(family => family.PublicId).IsUnique();
        builder.HasIndex(family => family.AccountId);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(family => family.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
