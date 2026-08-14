using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CollateralConfiguration : AqsatEntityConfiguration<Collateral>
{
    public override void Configure(EntityTypeBuilder<Collateral> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.SayadId).HasMaxLength(30);
        builder.Property(c => c.BankName).HasMaxLength(80);
        builder.Property(c => c.Amount).HasPrecision(18, 0);
        builder.Property(c => c.ColorCode).HasMaxLength(20);

        builder.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId);

        builder.HasIndex(c => new { c.AgencyId, c.PolicyId });
        builder.HasIndex(c => new { c.AgencyId, c.DueDate });
    }
}
