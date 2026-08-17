using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ImportColumnMappingConfiguration : AqsatEntityConfiguration<ImportColumnMapping>
{
    public override void Configure(EntityTypeBuilder<ImportColumnMapping> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.ImportType).HasMaxLength(60).IsRequired();
        builder.Property(m => m.MappingJson).IsRequired();

        // One saved mapping per (agency, import type) — saving again overwrites rather than
        // accumulating history; there is no requirement to keep prior mappings.
        builder.HasIndex(m => new { m.AgencyId, m.ImportType }).IsUnique();
    }
}
