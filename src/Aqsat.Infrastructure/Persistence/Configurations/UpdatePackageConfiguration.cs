using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class UpdatePackageConfiguration : AqsatEntityConfiguration<UpdatePackage>
{
    public override void Configure(EntityTypeBuilder<UpdatePackage> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Version).HasMaxLength(30).IsRequired();
        builder.Property(p => p.ReleaseNotesFa).HasMaxLength(4000).IsRequired();
        builder.Property(p => p.ImageTag).HasMaxLength(300).IsRequired();
        builder.Property(p => p.Sha256).HasMaxLength(71).IsRequired(); // "sha256:" + 64 hex chars
        builder.Property(p => p.SignatureBase64).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.MinimumFromVersion).HasMaxLength(30);

        builder.HasIndex(p => p.Version).IsUnique();
    }
}
