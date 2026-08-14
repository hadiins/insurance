using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : AqsatEntityConfiguration<Role>
{
    public override void Configure(EntityTypeBuilder<Role> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.Name).HasMaxLength(60).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
    }
}
