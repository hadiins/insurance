using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RenewalWatchConfiguration : AqsatEntityConfiguration<RenewalWatch>
{
    public override void Configure(EntityTypeBuilder<RenewalWatch> builder)
    {
        base.Configure(builder);

        builder.Property(w => w.ProspectName).HasMaxLength(120);
        builder.Property(w => w.ProspectMobile).HasMaxLength(15);
        builder.Property(w => w.CurrentInsurer).HasMaxLength(120);

        builder.HasOne(w => w.Customer).WithMany().HasForeignKey(w => w.CustomerId);
        builder.HasOne(w => w.InsuranceLine).WithMany().HasForeignKey(w => w.InsuranceLineId);
        builder.HasOne(w => w.Marketer).WithMany().HasForeignKey(w => w.MarketerId);
        builder.HasOne(w => w.Policy).WithMany().HasForeignKey(w => w.PolicyId);

        builder.HasIndex(w => new { w.AgencyId, w.CurrentExpiryDate });
        builder.HasIndex(w => new { w.AgencyId, w.Status });
    }
}
