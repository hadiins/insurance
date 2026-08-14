using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class InstallmentConfiguration : AqsatEntityConfiguration<Installment>
{
    public override void Configure(EntityTypeBuilder<Installment> builder)
    {
        base.Configure(builder);

        builder.Property(i => i.Amount).HasPrecision(18, 0);
        builder.Property(i => i.PaidAmount).HasPrecision(18, 0);

        builder.HasOne(i => i.Policy).WithMany().HasForeignKey(i => i.PolicyId);

        builder.HasIndex(i => new { i.AgencyId, i.PolicyId, i.SeqNo }).IsUnique();

        // Backs the settlement countdown query (docs/PHASE-1-SPEC.md §3.3) — Task 9's job to use it,
        // Task 3's job to make sure it exists once the schema is expensive to change.
        builder.HasIndex(i => new { i.AgencyId, i.Status, i.SettlementDeadline });
    }
}
