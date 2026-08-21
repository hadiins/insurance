using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ApiIrCallLogConfiguration : AqsatEntityConfiguration<ApiIrCallLog>
{
    public override void Configure(EntityTypeBuilder<ApiIrCallLog> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Service).HasMaxLength(40).IsRequired();
        builder.Property(c => c.CostToman).HasPrecision(18, 0);

        builder.HasIndex(c => new { c.AgencyId, c.Service, c.CalledAt });
    }
}
