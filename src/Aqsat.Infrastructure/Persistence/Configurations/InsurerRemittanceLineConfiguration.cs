using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class InsurerRemittanceLineConfiguration : AqsatEntityConfiguration<InsurerRemittanceLine>
{
    public override void Configure(EntityTypeBuilder<InsurerRemittanceLine> builder)
    {
        base.Configure(builder);

        builder.Property(l => l.Amount).HasPrecision(18, 0);

        builder.HasOne(l => l.InsurerRemittance).WithMany(r => r.Lines).HasForeignKey(l => l.InsurerRemittanceId);
        builder.HasOne(l => l.Policy).WithMany().HasForeignKey(l => l.PolicyId);
        builder.HasOne(l => l.Installment).WithMany().HasForeignKey(l => l.InstallmentId);

        builder.HasIndex(l => new { l.AgencyId, l.InsurerRemittanceId });
        builder.HasIndex(l => new { l.AgencyId, l.PolicyId });

        // An installment must never be remitted twice — the second attempt is a bug, not a
        // duplicate submission to swallow (unlike Payment's own idempotency index).
        builder.HasIndex(l => l.InstallmentId).IsUnique().HasFilter("[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");
    }
}
