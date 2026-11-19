using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Sessions;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "identity", table =>
        {
            table.HasCheckConstraint("CK_RefreshTokens_TokenHash", "LEN([TokenHash]) = 64 AND [TokenHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'");
            table.HasCheckConstraint("CK_RefreshTokens_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.HasCheckConstraint("CK_RefreshTokens_ConsumedAt", "[ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]");
            table.HasCheckConstraint("CK_RefreshTokens_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([ConsumedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ConsumedAtUtc]) = 0)");
        });
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedOnAdd();
        builder.Property(token => token.TokenHash).HasColumnType("char(64)").IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(token => token.CreatedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(token => token.ExpiresAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(token => token.ConsumedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        // Even an expired row keeps this slot until an authorized workflow consumes it.
        builder.HasIndex(token => token.TokenFamilyId).IsUnique().HasFilter("[ConsumedAtUtc] IS NULL");
        builder.HasOne<TokenFamily>().WithMany().HasForeignKey(token => token.TokenFamilyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
