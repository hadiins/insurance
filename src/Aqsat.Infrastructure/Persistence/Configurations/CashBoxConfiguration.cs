using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CashBoxConfiguration : AqsatEntityConfiguration<CashBox>
{
    public override void Configure(EntityTypeBuilder<CashBox> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.OpeningBalance).HasPrecision(18, 0);
    }
}
