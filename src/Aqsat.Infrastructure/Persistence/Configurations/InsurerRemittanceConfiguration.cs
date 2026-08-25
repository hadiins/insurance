using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class InsurerRemittanceConfiguration : AqsatEntityConfiguration<InsurerRemittance>
{
    public override void Configure(EntityTypeBuilder<InsurerRemittance> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.Amount).HasPrecision(18, 0);
        builder.Property(r => r.ReferenceNo).HasMaxLength(60);

        builder.HasOne(r => r.CashBox).WithMany().HasForeignKey(r => r.CashBoxId);
        builder.HasOne(r => r.BankAccount).WithMany().HasForeignKey(r => r.BankAccountId);

        builder.HasIndex(r => new { r.AgencyId, r.Date });
    }
}
