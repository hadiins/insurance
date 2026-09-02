using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CreditReportConfiguration : AqsatEntityConfiguration<CreditReport>
{
    public override void Configure(EntityTypeBuilder<CreditReport> builder)
    {
        base.Configure(builder);

        builder.HasOne(r => r.Policy)
            .WithMany()
            .HasForeignKey(r => r.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // The wizard's verification step reads the report by policy (rule 3/5 — AgencyId first).
        builder.HasIndex(r => new { r.AgencyId, r.PolicyId });
        builder.HasIndex(r => new { r.AgencyId, r.CustomerId });
    }
}
