using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CollectionContactConfiguration : AqsatEntityConfiguration<CollectionContact>
{
    public override void Configure(EntityTypeBuilder<CollectionContact> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.PromisedAmount).HasPrecision(18, 0);

        builder.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId);
        builder.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId);
        builder.HasOne(c => c.Installment).WithMany().HasForeignKey(c => c.InstallmentId);

        // AgencyId leads every index (rule 3), and these three cover the FK indexes rule 5 requires.
        builder.HasIndex(c => new { c.AgencyId, c.PolicyId, c.OccurredAt });
        builder.HasIndex(c => new { c.AgencyId, c.CustomerId, c.OccurredAt });
        builder.HasIndex(c => new { c.AgencyId, c.InstallmentId });

        // A promise is both a date and an amount, or neither — the write service ties this to
        // Outcome == Promised; the database refuses a half-written promise either way.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_CollectionContacts_Promise",
            "([PromisedOn] IS NULL AND [PromisedAmount] IS NULL) " +
            "OR ([PromisedOn] IS NOT NULL AND [PromisedAmount] IS NOT NULL)"));
    }
}
