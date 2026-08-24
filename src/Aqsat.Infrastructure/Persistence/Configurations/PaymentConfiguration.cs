using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : AqsatEntityConfiguration<Payment>
{
    public override void Configure(EntityTypeBuilder<Payment> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Amount).HasPrecision(18, 0);
        builder.Property(p => p.Method).HasMaxLength(30).IsRequired();
        builder.Property(p => p.ReferenceNo).HasMaxLength(60);

        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId);

        builder.HasOne(p => p.CashBox).WithMany().HasForeignKey(p => p.CashBoxId);
        builder.HasOne(p => p.BankAccount).WithMany().HasForeignKey(p => p.BankAccountId);

        builder.HasIndex(p => new { p.AgencyId, p.CustomerId });
        builder.HasIndex(p => new { p.AgencyId, p.CashBoxId });
        builder.HasIndex(p => new { p.AgencyId, p.BankAccountId });

        // Idempotency (docs/CONCURRENCY.md): a duplicate submission is caught by this unique index
        // and treated as success, never as an error. AgencyId leads per CLAUDE.md rule 3 — this
        // scopes the dedupe key per tenant, which is the correct boundary anyway.
        builder.HasIndex(p => new { p.AgencyId, p.InstallmentIdHint, p.PaidOn, p.Amount })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
