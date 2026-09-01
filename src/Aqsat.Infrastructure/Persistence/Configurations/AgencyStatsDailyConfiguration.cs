using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class AgencyStatsDailyConfiguration : AqsatEntityConfiguration<AgencyStatsDaily>
{
    public override void Configure(EntityTypeBuilder<AgencyStatsDaily> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.SmsCostToman).HasPrecision(18, 0);
        builder.Property(s => s.InquiryRevenueToman).HasPrecision(18, 0);
        builder.Property(s => s.InquiryCallCostToman).HasPrecision(18, 0);

        builder.HasOne(s => s.Agency)
            .WithMany()
            .HasForeignKey(s => s.AgencyId);

        // One row per (agency, day) — the rollup job upserts into this, never deletes
        // (CLAUDE.md rule 7: a day whose activity drops to zero just gets zeroed out).
        builder.HasIndex(s => new { s.AgencyId, s.StatDate }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
