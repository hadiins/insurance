using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RiskNetworkSettingsConfiguration : AqsatEntityConfiguration<RiskNetworkSettings>
{
    public override void Configure(EntityTypeBuilder<RiskNetworkSettings> builder)
    {
        base.Configure(builder);

        // Singleton by convention (like ApiIrSettings): at most one live row ever exists — the
        // upsert path queries first instead of relying on a unique index.
    }
}
