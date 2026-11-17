using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.ControlPlane.Infrastructure.Bootstrap;

public sealed class BootstrapCompletionConfiguration : IEntityTypeConfiguration<BootstrapCompletion>
{
    public void Configure(EntityTypeBuilder<BootstrapCompletion> builder)
    {
        builder.ToTable("BootstrapCompletion", "controlplane", table =>
            table.HasCheckConstraint("CK_BootstrapCompletion_Singleton", "[Id] = 1"));
        builder.HasKey(completion => completion.Id);
        builder.Property(completion => completion.Id).ValueGeneratedNever();
        builder.Property(completion => completion.CompletedAtUtc)
            .HasColumnType("datetimeoffset(7)").HasPrecision(7).ValueGeneratedNever().IsRequired();
    }
}
