using Aqsat.Application.Common;
using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>Needs an IFieldEncryptor instance for the NationalId converter, so — like
/// CustomerConfiguration — it can't be discovered via the assembly scan and is applied explicitly
/// in AppDbContext.OnModelCreating.</summary>
public sealed class MarketerConfiguration(IFieldEncryptor encryptor) : AqsatEntityConfiguration<Marketer>
{
    public override void Configure(EntityTypeBuilder<Marketer> builder)
    {
        base.Configure(builder);

        builder.Property(m => m.FullName).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Mobile).HasMaxLength(15).IsRequired();

        // Encrypted at rest (CLAUDE.md rule 12), same converter as CustomerConfiguration.
        builder.Property(m => m.NationalId)
            .HasConversion(new ValueConverter<string?, byte[]?>(
                v => v == null ? null : encryptor.Encrypt(v),
                v => v == null ? null : encryptor.Decrypt(v)));

        builder.HasOne(m => m.AppUser).WithMany().HasForeignKey(m => m.AppUserId);

        // docs/PHASE-1-SPEC.md §2.2 — one marketer, one agency, enforced in the database.
        builder.HasIndex(m => m.AppUserId).IsUnique().HasFilter("[AppUserId] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(m => new { m.AgencyId, m.Mobile });
    }
}
