using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class NetworkRiskPlateIndexConfiguration : AqsatEntityConfiguration<NetworkRiskPlateIndex>
{
    public override void Configure(EntityTypeBuilder<NetworkRiskPlateIndex> builder)
    {
        base.Configure(builder);

        builder.HasOne(i => i.Agency)
            .WithMany()
            .HasForeignKey(i => i.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.PlateNormalized).HasMaxLength(30).IsRequired();

        // One row per (source agency, plate) — insert-only, the sync skips what exists.
        builder.HasIndex(i => new { i.AgencyId, i.PlateNormalized }).IsUnique().HasFilter("[IsDeleted] = 0");
        // The cross-agency search path (same rule-3 deviation rationale as NetworkRiskProfile).
        builder.HasIndex(i => i.PlateNormalized);
    }
}
