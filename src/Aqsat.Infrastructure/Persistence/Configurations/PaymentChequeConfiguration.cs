using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PaymentChequeConfiguration : AqsatEntityConfiguration<PaymentCheque>
{
    public override void Configure(EntityTypeBuilder<PaymentCheque> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.ChequeNumber).HasMaxLength(40).IsRequired();
        builder.Property(c => c.BankName).HasMaxLength(80).IsRequired();
        builder.Property(c => c.PresenterName).HasMaxLength(120).IsRequired();

        builder.HasOne(c => c.Payment).WithMany().HasForeignKey(c => c.PaymentId);
        builder.HasOne(c => c.CashBox).WithMany().HasForeignKey(c => c.CashBoxId);
        builder.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId);

        builder.HasIndex(c => c.PaymentId).IsUnique();
        builder.HasIndex(c => new { c.AgencyId, c.PolicyId });
        builder.HasIndex(c => new { c.AgencyId, c.Status });
    }
}
