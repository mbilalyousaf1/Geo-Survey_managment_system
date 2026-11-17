using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

public sealed class PendingTotpEnrollmentConfiguration : IEntityTypeConfiguration<PendingTotpEnrollment>
{
    public void Configure(EntityTypeBuilder<PendingTotpEnrollment> builder)
    {
        builder.ToTable("PendingTotpEnrollments", "identity");
        builder.HasKey(enrollment => enrollment.AccountId);
        builder.Property(enrollment => enrollment.AccountId).ValueGeneratedNever();
        builder.Property(enrollment => enrollment.ProtectedSecret).IsRequired();
        builder.Property(enrollment => enrollment.CreatedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasIndex(enrollment => enrollment.ChallengeId).IsUnique();
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(enrollment => enrollment.AccountId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<MfaChallenge>().WithMany()
            .HasForeignKey(enrollment => new { enrollment.ChallengeId, enrollment.AccountId })
            .HasPrincipalKey(challenge => new { challenge.Id, challenge.AccountId }).OnDelete(DeleteBehavior.NoAction);
    }
}
