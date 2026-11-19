using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

public sealed class ConfirmedTotpCredentialConfiguration : IEntityTypeConfiguration<ConfirmedTotpCredential>
{
    public void Configure(EntityTypeBuilder<ConfirmedTotpCredential> builder)
    {
        builder.ToTable("TotpCredentials", "identity");
        builder.HasKey(credential => credential.AccountId);
        builder.Property(credential => credential.AccountId).ValueGeneratedNever();
        builder.Property(credential => credential.ProtectedSecret).IsRequired();
        builder.Property(credential => credential.ConfirmedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(credential => credential.AccountId).OnDelete(DeleteBehavior.NoAction);
    }
}
