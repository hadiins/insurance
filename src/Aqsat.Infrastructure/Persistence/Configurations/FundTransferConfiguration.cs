using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class FundTransferConfiguration : AqsatEntityConfiguration<FundTransfer>
{
    public override void Configure(EntityTypeBuilder<FundTransfer> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Amount).HasPrecision(18, 0);
        builder.Property(t => t.Note).HasMaxLength(200);

        builder.HasOne(t => t.FromCashBox).WithMany().HasForeignKey(t => t.FromCashBoxId);
        builder.HasOne(t => t.FromBankAccount).WithMany().HasForeignKey(t => t.FromBankAccountId);
        builder.HasOne(t => t.ToCashBox).WithMany().HasForeignKey(t => t.ToCashBoxId);
        builder.HasOne(t => t.ToBankAccount).WithMany().HasForeignKey(t => t.ToBankAccountId);

        builder.HasIndex(t => new { t.AgencyId, t.Date });
        builder.HasIndex(t => new { t.AgencyId, t.FromCashBoxId });
        builder.HasIndex(t => new { t.AgencyId, t.FromBankAccountId });
        builder.HasIndex(t => new { t.AgencyId, t.ToCashBoxId });
        builder.HasIndex(t => new { t.AgencyId, t.ToBankAccountId });
    }
}

public sealed class CommissionPayoutConfiguration : AqsatEntityConfiguration<CommissionPayout>
{
    public override void Configure(EntityTypeBuilder<CommissionPayout> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Amount).HasPrecision(18, 0);
        builder.Property(p => p.ReferenceNo).HasMaxLength(60);

        builder.HasOne(p => p.Marketer).WithMany().HasForeignKey(p => p.MarketerId);
        builder.HasOne(p => p.CashBox).WithMany().HasForeignKey(p => p.CashBoxId);
        builder.HasOne(p => p.BankAccount).WithMany().HasForeignKey(p => p.BankAccountId);

        builder.HasIndex(p => new { p.AgencyId, p.PaidOn });
        builder.HasIndex(p => new { p.AgencyId, p.MarketerId });
        builder.HasIndex(p => new { p.AgencyId, p.CashBoxId });
        builder.HasIndex(p => new { p.AgencyId, p.BankAccountId });
    }
}
