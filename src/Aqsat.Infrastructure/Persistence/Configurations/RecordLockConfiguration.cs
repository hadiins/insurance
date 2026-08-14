using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class RecordLockConfiguration : IEntityTypeConfiguration<RecordLock>
{
    public void Configure(EntityTypeBuilder<RecordLock> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(l => l.LockedByDisplayName).HasMaxLength(120).IsRequired();
        builder.Property(l => l.ForceReleaseReason).HasMaxLength(500);

        // docs/CONCURRENCY.md's own filtered-index example compares ExpiresAt to SYSDATETIMEOFFSET(),
        // which SQL Server does not allow in a filtered index predicate (must be deterministic).
        // This filters only on ForceReleasedAt; "not yet expired" is enforced by the atomic MERGE
        // acquire logic (Task 11), same as the doc's own MERGE statement already does.
        builder.HasIndex(l => new { l.AgencyId, l.EntityType, l.EntityId })
            .IsUnique()
            .HasFilter("[ForceReleasedAt] IS NULL");
    }
}
