using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CustomerPaymentLinkConfiguration : AqsatEntityConfiguration<CustomerPaymentLink>
{
    public override void Configure(EntityTypeBuilder<CustomerPaymentLink> builder)
    {
        base.Configure(builder);

        builder.Property(l => l.Token).HasMaxLength(43).IsRequired();

        // The public pay page's scoped entry point: token lookup, AgencyId leading (rule 3).
        // Filtered so a soft-deleted row's token can never resolve.
        builder.HasIndex(l => new { l.AgencyId, l.Token })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // One ACTIVE link per customer — the reminder job reuses the existing link; two would mean
        // two live tokens for the same person, and revocation would only kill one of them.
        builder.HasIndex(l => new { l.AgencyId, l.CustomerId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 1");

        builder.HasOne(l => l.Customer)
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
