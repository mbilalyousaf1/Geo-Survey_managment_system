using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

public sealed class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> builder)
    {
        builder.ToTable("MfaRecoveryCodes", "identity", table => table.HasCheckConstraint(
            "CK_MfaRecoveryCodes_ConsumedAt", "[ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]"));
        builder.HasKey(code => new { code.AccountId, code.CodeHash });
        builder.Property(code => code.CodeHash).HasColumnType("char(64)").IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(code => code.CreatedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(code => code.ConsumedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(code => code.AccountId).OnDelete(DeleteBehavior.NoAction);
    }
}
