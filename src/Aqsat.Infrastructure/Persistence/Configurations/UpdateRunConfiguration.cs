using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class UpdateRunConfiguration : AqsatEntityConfiguration<UpdateRun>
{
    public override void Configure(EntityTypeBuilder<UpdateRun> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.FromVersion).HasMaxLength(30).IsRequired();
        builder.Property(r => r.ToVersion).HasMaxLength(30).IsRequired();
        builder.Property(r => r.CurrentStage).HasMaxLength(60).IsRequired();
        builder.Property(r => r.BackupPath).HasMaxLength(500);
        builder.Property(r => r.ErrorCode).HasMaxLength(30);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);
        builder.Property(r => r.ErrorDetail).HasColumnType("nvarchar(max)");

        builder.HasOne(r => r.Package).WithMany().HasForeignKey(r => r.PackageId);

        builder.HasIndex(r => r.StartedAt);
        builder.HasIndex(r => r.Status);
    }
}
