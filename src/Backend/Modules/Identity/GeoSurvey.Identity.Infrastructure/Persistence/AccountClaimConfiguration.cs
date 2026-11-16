using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

public sealed class AccountClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<int>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<int>> builder)
    {
        builder.ToTable("AccountClaims", "identity");
        builder.HasKey(claim => claim.Id);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(claim => claim.UserId)
            .IsRequired().OnDelete(DeleteBehavior.NoAction);
    }
}
