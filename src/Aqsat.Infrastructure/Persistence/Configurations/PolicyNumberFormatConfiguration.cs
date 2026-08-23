using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PolicyNumberFormatConfiguration : AqsatEntityConfiguration<PolicyNumberFormat>
{
    public override void Configure(EntityTypeBuilder<PolicyNumberFormat> builder)
    {
        base.Configure(builder);

        builder.Property(f => f.InsurerName).HasMaxLength(80).IsRequired();
        builder.Property(f => f.Pattern).HasMaxLength(60).IsRequired();
        builder.Property(f => f.Separator).HasMaxLength(5).IsRequired();

        builder.HasIndex(f => new { f.AgencyId, f.InsurerName }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
