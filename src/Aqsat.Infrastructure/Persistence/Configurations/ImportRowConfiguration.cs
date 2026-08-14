using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ImportRowConfiguration : AqsatEntityConfiguration<ImportRow>
{
    public override void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);

        builder.HasOne(r => r.ImportBatch).WithMany(b => b.Rows).HasForeignKey(r => r.ImportBatchId);

        builder.HasIndex(r => new { r.AgencyId, r.ImportBatchId });
    }
}
