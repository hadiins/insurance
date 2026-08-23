using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class InsuranceLineCodeConfiguration : AqsatEntityConfiguration<InsuranceLineCode>
{
    public override void Configure(EntityTypeBuilder<InsuranceLineCode> builder)
    {
        base.Configure(builder);

        builder.Property(l => l.InsurerName).HasMaxLength(80).IsRequired();
        builder.Property(l => l.Code).HasMaxLength(10).IsRequired();

        builder.HasOne(l => l.InsuranceLine).WithMany().HasForeignKey(l => l.InsuranceLineId);

        // A code must not map to two lines, and a line must not hold two codes, for the same
        // insurer within one agency.
        builder.HasIndex(l => new { l.AgencyId, l.InsurerName, l.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(l => new { l.AgencyId, l.InsurerName, l.InsuranceLineId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
