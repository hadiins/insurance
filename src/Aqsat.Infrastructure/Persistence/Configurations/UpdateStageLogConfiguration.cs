using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class UpdateStageLogConfiguration : AqsatEntityConfiguration<UpdateStageLog>
{
    public override void Configure(EntityTypeBuilder<UpdateStageLog> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.StageName).HasMaxLength(60).IsRequired();
        builder.Property(s => s.Output).HasColumnType("nvarchar(max)");

        builder.HasOne(s => s.Run).WithMany().HasForeignKey(s => s.RunId);

        builder.HasIndex(s => new { s.RunId, s.StageNo }).IsUnique();
    }
}
