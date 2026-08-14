using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>
/// Immutable append-only — bigint identity PK (not the Entity Id/BizId pattern; this table is
/// huge). No RowVersion, no soft delete: audit rows are never updated after insert.
/// </summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityColumn();

        builder.Property(a => a.UserDisplayName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(400).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(45);

        // CLAUDE.md rule 3 (AgencyId first) takes precedence over docs/PHASE-1-SPEC.md's example
        // (PolicyId, OccurredAt DESC) — see plan notes.
        builder.HasIndex(a => new { a.AgencyId, a.PolicyId, a.OccurredAt })
            .IsDescending(false, false, true);
    }
}
