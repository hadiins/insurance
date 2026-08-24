using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class AgencyCommissionRateConfiguration : AqsatEntityConfiguration<AgencyCommissionRate>
{
    public override void Configure(EntityTypeBuilder<AgencyCommissionRate> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.RatePercent).HasPrecision(9, 4);

        builder.HasOne(r => r.InsuranceLine).WithMany().HasForeignKey(r => r.InsuranceLineId);

        builder.HasIndex(r => new { r.AgencyId, r.InsuranceLineId });
    }
}
