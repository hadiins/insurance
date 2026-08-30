using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ApiIrSettingsConfiguration : AqsatEntityConfiguration<ApiIrSettings>
{
    public override void Configure(EntityTypeBuilder<ApiIrSettings> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.ApiKey).HasMaxLength(200);

        // Singleton-by-convention: the panel's GET/PUT is the only writer and reads-before-write,
        // so two rows cannot arise from ordinary use (Platform.Owner is a single human, not a
        // concurrent workload). No unique index is added — there is no natural key to constrain.
    }
}