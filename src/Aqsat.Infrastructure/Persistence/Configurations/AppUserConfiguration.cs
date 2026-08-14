using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class AppUserConfiguration : AqsatEntityConfiguration<AppUser>
{
    public override void Configure(EntityTypeBuilder<AppUser> builder)
    {
        base.Configure(builder);

        builder.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Mobile).HasMaxLength(15).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();

        builder.HasIndex(u => u.Mobile).IsUnique();
    }
}
