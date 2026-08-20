using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class MarketerRateConfiguration : AqsatEntityConfiguration<MarketerRate>
{
    public override void Configure(EntityTypeBuilder<MarketerRate> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.RatePercent).HasPrecision(9, 4);

        builder.HasOne(r => r.Marketer).WithMany().HasForeignKey(r => r.MarketerId);
        builder.HasOne(r => r.InsuranceLine).WithMany().HasForeignKey(r => r.InsuranceLineId);

        builder.HasIndex(r => new { r.AgencyId, r.MarketerId, r.InsuranceLineId });
    }
}
