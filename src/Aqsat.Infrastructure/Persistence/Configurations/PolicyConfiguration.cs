using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PolicyConfiguration : AqsatEntityConfiguration<Policy>
{
    public override void Configure(EntityTypeBuilder<Policy> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.PolicyNumber).HasMaxLength(40).IsRequired();
        builder.Property(p => p.ContractName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.TotalPremium).HasPrecision(18, 0);
        builder.Property(p => p.DownPayment).HasPrecision(18, 0);

        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId);
        builder.HasOne(p => p.Vehicle).WithMany().HasForeignKey(p => p.VehicleId);
        builder.HasOne(p => p.ImportBatch).WithMany().HasForeignKey(p => p.ImportBatchId);

        builder.HasIndex(p => new { p.AgencyId, p.PolicyNumber }).IsUnique();
        builder.HasIndex(p => new { p.AgencyId, p.CustomerId });
    }
}
