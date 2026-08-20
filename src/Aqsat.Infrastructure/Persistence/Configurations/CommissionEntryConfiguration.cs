using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CommissionEntryConfiguration : AqsatEntityConfiguration<CommissionEntry>
{
    public override void Configure(EntityTypeBuilder<CommissionEntry> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.BasePortion).HasPrecision(18, 0);
        builder.Property(c => c.RatePercent).HasPrecision(9, 4);
        builder.Property(c => c.Amount).HasPrecision(18, 0);

        builder.HasOne(c => c.Marketer).WithMany().HasForeignKey(c => c.MarketerId);
        builder.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId);
        builder.HasOne(c => c.Installment).WithMany().HasForeignKey(c => c.InstallmentId);

        builder.HasIndex(c => new { c.AgencyId, c.MarketerId, c.Status });
        builder.HasIndex(c => new { c.AgencyId, c.PolicyId });
        builder.HasIndex(c => new { c.AgencyId, c.InstallmentId });
    }
}
