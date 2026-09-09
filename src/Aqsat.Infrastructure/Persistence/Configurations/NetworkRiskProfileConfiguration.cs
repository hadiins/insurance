using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class NetworkRiskProfileConfiguration : AqsatEntityConfiguration<NetworkRiskProfile>
{
    public override void Configure(EntityTypeBuilder<NetworkRiskProfile> builder)
    {
        base.Configure(builder);

        builder.HasOne(p => p.Agency)
            .WithMany()
            .HasForeignKey(p => p.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Customer)
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.OnTimeRatePercent).HasPrecision(5, 2);

        // The upsert path: one live row per (source agency, national-ID hash).
        builder.HasIndex(p => new { p.AgencyId, p.NationalIdHash }).IsUnique().HasFilter("[IsDeleted] = 0");
        // The cross-agency search path. Deliberately NOT led by AgencyId (a documented deviation
        // from rule 3): this table is RLS-exempt, so the query never carries an agency filter —
        // NationalIdHash alone is the whole predicate.
        builder.HasIndex(p => p.NationalIdHash);
        // Rule 5: every FK gets its own index (same single-column precedent as RiskWarnings.CustomerId).
        builder.HasIndex(p => p.CustomerId);
    }
}
