using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RiskWarningConfiguration : AqsatEntityConfiguration<RiskWarning>
{
    public override void Configure(EntityTypeBuilder<RiskWarning> builder)
    {
        base.Configure(builder);

        builder.HasOne(w => w.Customer)
            .WithMany()
            .HasForeignKey(w => w.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Assessment)
            .WithMany()
            .HasForeignKey(w => w.AssessmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The warnings page lists unread first, newest first.
        builder.HasIndex(w => new { w.AgencyId, w.IsRead, w.CreatedAt }).IsDescending();

        builder.Property(w => w.Message).HasMaxLength(512).IsRequired();
    }
}
