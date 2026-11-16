using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

public sealed class AccountTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<int>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<int>> builder)
    {
        builder.ToTable("AccountTokens", "identity");
        builder.HasKey(token => new { token.UserId, token.LoginProvider, token.Name });
        builder.Property(token => token.LoginProvider).HasMaxLength(IdentityPersistenceOptions.MaximumKeyLength);
        builder.Property(token => token.Name).HasMaxLength(IdentityPersistenceOptions.MaximumKeyLength);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(token => token.UserId)
            .IsRequired().OnDelete(DeleteBehavior.NoAction);
    }
}
