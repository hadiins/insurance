using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration : AqsatEntityConfiguration<RolePermission>
{
    public override void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Permission).HasMaxLength(50).IsRequired();

        builder.HasOne(p => p.Role)
            .WithMany()
            .HasForeignKey(p => p.RoleId);

        builder.HasIndex(p => new { p.RoleId, p.Permission }).IsUnique();
    }
}
