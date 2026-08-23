using Aqsat.Application.Common;
using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration(IFieldEncryptor encryptor) : AqsatEntityConfiguration<Customer>
{
    public override void Configure(EntityTypeBuilder<Customer> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.ExternalCode).HasMaxLength(30).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Mobile).HasMaxLength(15);
        builder.Property(c => c.FirstName).HasMaxLength(60);
        builder.Property(c => c.LastName).HasMaxLength(80);
        builder.Property(c => c.FullNameLegacy).HasMaxLength(120);
        builder.Property(c => c.EmergencyMobile).HasMaxLength(15);
        builder.Property(c => c.Address).HasMaxLength(400);
        builder.Property(c => c.PostalCode).HasMaxLength(10);

        // docs/TASK-25-IDENTITY-VEHICLE.md §3 — persisted so it can be indexed; SQL Server computes
        // it, the app never writes it (IsProfileComplete has a private setter for exactly this).
        // NationalId's underlying column is varbinary (encrypted) but IS NOT NULL still works on it.
        builder.Property(c => c.IsProfileComplete)
            .HasComputedColumnSql(
                "CASE WHEN [NationalId] IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL " +
                "AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL " +
                "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true);

        // Encrypted at rest (CLAUDE.md rule 12) — plaintext only ever exists in memory.
        builder.Property(c => c.NationalId)
            .HasConversion(new ValueConverter<string?, byte[]?>(
                v => v == null ? null : encryptor.Encrypt(v),
                v => v == null ? null : encryptor.Decrypt(v)));

        builder.HasIndex(c => new { c.AgencyId, c.ExternalCode }).IsUnique();
        builder.HasIndex(c => new { c.AgencyId, c.NationalIdHash });
        // SQL Server rejects a filtered index whose filter predicate references a computed column
        // (error 10609), even a persisted one — the filter can only reference IsDeleted. The index
        // itself still covers IsProfileComplete for the "who's incomplete" query.
        builder.HasIndex(c => new { c.AgencyId, c.IsProfileComplete })
            .HasFilter("[IsDeleted] = 0");
    }
}
