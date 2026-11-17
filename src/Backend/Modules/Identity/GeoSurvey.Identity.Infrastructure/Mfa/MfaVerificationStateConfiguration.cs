using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

public sealed class MfaVerificationStateConfiguration : IEntityTypeConfiguration<MfaVerificationState>
{
    public void Configure(EntityTypeBuilder<MfaVerificationState> builder)
    {
        builder.ToTable("MfaVerificationStates", "identity", table =>
        {
            table.HasCheckConstraint("CK_MfaVerificationStates_Count", "[FailedAttemptCount] BETWEEN 0 AND 5");
            table.HasCheckConstraint("CK_MfaVerificationStates_Window", "([FailedAttemptCount] = 0 AND [FailureWindowStartedAtUtc] IS NULL) OR ([FailedAttemptCount] > 0 AND [FailureWindowStartedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_MfaVerificationStates_Block", "[BlockedUntilUtc] IS NULL OR [FailedAttemptCount] = 5");
            table.HasCheckConstraint("CK_MfaVerificationStates_Step", "[LastAcceptedStep] IS NULL OR [LastAcceptedStep] >= 0");
        });
        builder.HasKey(state => state.AccountId);
        builder.Property(state => state.AccountId).ValueGeneratedNever();
        builder.Property(state => state.FailureWindowStartedAtUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.Property(state => state.BlockedUntilUtc).HasColumnType("datetimeoffset(7)").HasPrecision(7);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(state => state.AccountId).OnDelete(DeleteBehavior.NoAction);
    }
}
