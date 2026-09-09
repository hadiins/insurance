using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RiskAssessmentConfiguration : AqsatEntityConfiguration<RiskAssessment>
{
    public override void Configure(EntityTypeBuilder<RiskAssessment> builder)
    {
        base.Configure(builder);

        builder.HasOne(a => a.Customer)
            .WithMany()
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // The customer-file risk tab reads the latest assessment, and history reads them all in
        // date order (rule 3/5 — AgencyId first).
        builder.HasIndex(a => new { a.AgencyId, a.CustomerId, a.CalculatedAt }).IsDescending();

        builder.Property(a => a.FactorsJson).IsRequired();
        builder.Property(a => a.TriggeredRulesJson).IsRequired();
        builder.Property(a => a.ModelVersion).HasMaxLength(32).IsRequired();
        builder.Property(a => a.ScoreSource).HasMaxLength(32).IsRequired();
    }
}
