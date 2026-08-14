using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RecordPresenceConfiguration : IEntityTypeConfiguration<RecordPresence>
{
    public void Configure(EntityTypeBuilder<RecordPresence> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).UseIdentityColumn();

        builder.Property(p => p.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(p => p.UserDisplayName).HasMaxLength(120).IsRequired();
        builder.Property(p => p.ConnectionId).HasMaxLength(100).IsRequired();

        builder.HasIndex(p => new { p.AgencyId, p.EntityType, p.EntityId, p.UserId }).IsUnique();
        builder.HasIndex(p => new { p.AgencyId, p.LastSeenAt });
    }
}
