using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>Discovered via the assembly scan like every other configuration: since national IDs
/// moved to plaintext storage (owner decision 2026-08-28) it no longer needs an IFieldEncryptor
/// instance and is no longer applied explicitly in AppDbContext.OnModelCreating.</summary>
public sealed class MarketerConfiguration : AqsatEntityConfiguration<Marketer>
{
    public override void Configure(EntityTypeBuilder<Marketer> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.FullName).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Mobile).HasMaxLength(15).IsRequired();

        // CLAUDE.md rule 12 (owner decision 2026-08-28) — plaintext, same as Customer.NationalId.
        builder.Property(m => m.NationalId).HasMaxLength(30);

        builder.HasOne(m => m.AppUser).WithMany().HasForeignKey(m => m.AppUserId);

        // docs/PHASE-1-SPEC.md §2.2 — one marketer, one agency, enforced in the database.
        builder.HasIndex(m => m.AppUserId).IsUnique().HasFilter("[AppUserId] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(m => new { m.AgencyId, m.Mobile });
    }
}
