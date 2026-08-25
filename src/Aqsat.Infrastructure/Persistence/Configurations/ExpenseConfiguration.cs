using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ExpenseConfiguration : AqsatEntityConfiguration<Expense>
{
    public override void Configure(EntityTypeBuilder<Expense> builder)
    {
        base.Configure(builder);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 0);

        builder.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId);
        builder.HasOne(e => e.CashBox).WithMany().HasForeignKey(e => e.CashBoxId);
        builder.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId);

        builder.HasIndex(e => new { e.AgencyId, e.Date });
        builder.HasIndex(e => new { e.AgencyId, e.CategoryId });
    }
}
