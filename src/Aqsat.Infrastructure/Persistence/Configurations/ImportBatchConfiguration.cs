using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ImportBatchConfiguration : AqsatEntityConfiguration<ImportBatch>
{
    public override void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        base.Configure(builder);

        builder.Property(b => b.FileName).HasMaxLength(260).IsRequired();
        builder.Property(b => b.FileHash).HasMaxLength(64).IsRequired();
    }
}
