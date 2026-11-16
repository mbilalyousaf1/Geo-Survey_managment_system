using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Identity.Infrastructure.Persistence;

public sealed class AccountLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<int>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<int>> builder)
    {
        builder.ToTable("AccountLogins", "identity");
        builder.HasKey(login => new { login.LoginProvider, login.ProviderKey });
        builder.Property(login => login.LoginProvider).HasMaxLength(IdentityPersistenceOptions.MaximumKeyLength);
        builder.Property(login => login.ProviderKey).HasMaxLength(IdentityPersistenceOptions.MaximumKeyLength);
        builder.HasOne<IdentityAccount>().WithMany().HasForeignKey(login => login.UserId)
            .IsRequired().OnDelete(DeleteBehavior.NoAction);
    }
}
