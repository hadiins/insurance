using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : AqsatEntityConfiguration<Organization>
{
    public override void Configure(EntityTypeBuilder<Organization> builder)
    {
        base.Configure(builder);

        builder.Property(o => o.Code).HasMaxLength(20).IsRequired();
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.InsurerName).HasMaxLength(80);

        builder.HasOne(o => o.Parent)
            .WithMany()
            .HasForeignKey(o => o.ParentId);
    }
}
