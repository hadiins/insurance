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
        builder.Property(p => p.NetPremium).HasPrecision(18, 0);
        builder.Property(p => p.ServiceFee).HasPrecision(18, 0);
        builder.Property(p => p.DownPayment).HasPrecision(18, 0);
        builder.Property(p => p.AgencyCommissionPercent).HasPrecision(9, 4);
        builder.Property(p => p.AgencyCommissionAmount).HasPrecision(18, 0);
        builder.Property(p => p.MarketerRatePercent).HasPrecision(9, 4);
        builder.Property(p => p.PreviousInsurer).HasMaxLength(120);
        builder.Property(p => p.PnLineCode).HasMaxLength(10);
        builder.Property(p => p.PnAgencyCode).HasMaxLength(20);
        builder.Property(p => p.PnSerial).HasMaxLength(20);
        builder.Property(p => p.PnParseNote).HasMaxLength(200);

        builder.HasOne(p => p.InsuranceLine).WithMany().HasForeignKey(p => p.InsuranceLineId);
        builder.HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId);
        builder.HasOne(p => p.Vehicle).WithMany().HasForeignKey(p => p.VehicleId);
        builder.HasOne(p => p.PropertySubject).WithMany().HasForeignKey(p => p.PropertySubjectId);
        builder.HasOne(p => p.Marketer).WithMany().HasForeignKey(p => p.MarketerId);
        builder.HasOne(p => p.ImportBatch).WithMany().HasForeignKey(p => p.ImportBatchId);

        builder.HasIndex(p => new { p.AgencyId, p.PolicyNumber }).IsUnique();
        builder.HasIndex(p => new { p.AgencyId, p.CustomerId });
        builder.HasIndex(p => new { p.AgencyId, p.InsuranceLineId });
        builder.HasIndex(p => new { p.AgencyId, p.MarketerId });
        builder.HasIndex(p => new { p.AgencyId, p.PnYear, p.PnSerial });
    }
}
