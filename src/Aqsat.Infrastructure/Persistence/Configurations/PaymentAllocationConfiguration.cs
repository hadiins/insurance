using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PaymentAllocationConfiguration : AqsatEntityConfiguration<PaymentAllocation>
{
    public override void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        base.Configure(builder);

        builder.Property(a => a.Amount).HasPrecision(18, 0);

        builder.HasOne(a => a.Payment).WithMany(p => p.Allocations).HasForeignKey(a => a.PaymentId);
        builder.HasOne(a => a.Installment).WithMany().HasForeignKey(a => a.InstallmentId);

        builder.HasIndex(a => new { a.AgencyId, a.PaymentId });
        builder.HasIndex(a => new { a.AgencyId, a.InstallmentId });
    }
}
