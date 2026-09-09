using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CustomerCreditLimitConfiguration : AqsatEntityConfiguration<CustomerCreditLimit>
{
    public override void Configure(EntityTypeBuilder<CustomerCreditLimit> builder)
    {
        base.Configure(builder);

        builder.HasOne(l => l.Customer)
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One override per customer — enforced by a real unique index (CLAUDE.md "one marketer, one
        // agency" family of rules), not by UI validation.
        builder.HasIndex(l => new { l.AgencyId, l.CustomerId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.Property(l => l.Reason).HasMaxLength(512);
    }
}
