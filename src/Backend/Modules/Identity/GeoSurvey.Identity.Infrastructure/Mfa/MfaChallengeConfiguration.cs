using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

public sealed class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.ToTable("MfaChallenges", "identity", table =>
        {
            table.HasCheckConstraint("CK_MfaChallenges_Purpose", "[Purpose] IN (1, 2)");
            table.HasCheckConstraint("CK_MfaChallenges_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
        });
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).ValueGeneratedOnAdd();
        builder.Property(challenge => challenge.Purpose).HasConversion<int>();
        builder.Property(challenge => challenge.TokenHash).HasColumnType("char(64)").IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(challenge => challenge.SecurityStamp).IsRequired();
        builder.Property(challenge => challenge.CreatedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(challenge => challenge.ExpiresAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(challenge => challenge.ConsumedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(challenge => challenge.RevokedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasIndex(challenge => challenge.TokenHash).IsUnique();
        builder.HasIndex(challenge => new { challenge.AccountId, challenge.Purpose }).IsUnique()
            .HasFilter("[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(challenge => challenge.AccountId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
