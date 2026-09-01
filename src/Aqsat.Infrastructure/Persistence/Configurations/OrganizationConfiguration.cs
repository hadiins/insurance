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
        builder.Property(o => o.Province).HasMaxLength(50);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.InsurerName).HasMaxLength(80);
        builder.Property(o => o.AgencyCode).HasMaxLength(20);

        builder.HasIndex(o => o.Code).IsUnique().HasFilter("[IsDeleted] = 0");

        // The owner-side agency list filters by (level, province) and sorts by name across every
        // tenant — Organization is the tenant itself so it carries no AgencyId and no RLS; without
        // these the 60k-row list is a scan + sort on every page.
        builder.HasIndex(o => new { o.Level, o.Province });
        builder.HasIndex(o => new { o.Level, o.Name });

        builder.HasOne(o => o.Parent)
            .WithMany()
            .HasForeignKey(o => o.ParentId);
    }
}
