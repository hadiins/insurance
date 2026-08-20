using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class EndorsementConfiguration : AqsatEntityConfiguration<Endorsement>
{
    public override void Configure(EntityTypeBuilder<Endorsement> builder)
    {
        base.Configure(builder);

        builder.Property(e => e.EndorsementNo).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(60).IsRequired();
        builder.Property(e => e.PremiumDelta).HasPrecision(18, 0);
        builder.Property(e => e.ServiceFeeDelta).HasPrecision(18, 0);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne(e => e.Policy).WithMany().HasForeignKey(e => e.PolicyId);

        builder.HasIndex(e => new { e.AgencyId, e.PolicyId });
    }
}
