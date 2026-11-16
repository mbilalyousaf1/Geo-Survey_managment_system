using GeoSurvey.Organizations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoSurvey.Organizations.Infrastructure.Persistence;

/// <summary>Storage contract composed explicitly by the global control-plane context.</summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", "organization");
        builder.HasKey(organization => organization.Id);
        builder.Property(organization => organization.Id).ValueGeneratedOnAdd();
        builder.Property(organization => organization.PublicId)
            .HasColumnType("uniqueidentifier").ValueGeneratedNever().IsRequired();
        builder.HasIndex(organization => organization.PublicId).IsUnique();
        builder.Property(organization => organization.Code)
            .HasConversion(code => code.Value, value => OrganizationCode.Create(value))
            .HasColumnType("nvarchar(50)").HasMaxLength(50)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.HasIndex(organization => organization.Code).IsUnique();
        builder.Property(organization => organization.DisplayName)
            .HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(organization => organization.CreatedAtUtc)
            .HasColumnType("datetimeoffset(7)").HasPrecision(7).ValueGeneratedNever().IsRequired();
    }
}
