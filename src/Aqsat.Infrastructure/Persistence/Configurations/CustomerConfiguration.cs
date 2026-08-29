using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : AqsatEntityConfiguration<Customer>
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

        // CLAUDE.md rule 12 (owner decision 2026-08-28) — the national ID is stored as PLAINTEXT
        // (nvarchar(30)): it is the issuance wizard's entry key and must be directly queryable. The
        // keyed HMAC NationalIdHash is still maintained alongside it (dedupe/audit paths), so this
        // configuration no longer needs an IFieldEncryptor and joins the plain assembly scan.
        builder.Property(c => c.NationalId).HasMaxLength(30);

        // docs/TASK-25-IDENTITY-VEHICLE.md §3 — persisted so it can be indexed; SQL Server computes
        // it, the app never writes it (IsProfileComplete has a private setter for exactly this).
        builder.Property(c => c.IsProfileComplete)
            .HasComputedColumnSql(
                "CASE WHEN [NationalId] IS NOT NULL AND [Mobile] IS NOT NULL AND [Address] IS NOT NULL " +
                "AND [PostalCode] IS NOT NULL AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL " +
                "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true);

        builder.HasIndex(c => new { c.AgencyId, c.ExternalCode }).IsUnique();
        builder.HasIndex(c => new { c.AgencyId, c.NationalIdHash });
        // The issuance wizard's step-1 lookup (GET /api/customers/lookup?nationalId=...) is an
        // equality search on the plaintext national ID (rule 3: AgencyId leads every index).
        builder.HasIndex(c => new { c.AgencyId, c.NationalId });
        // SQL Server rejects a filtered index whose filter predicate references a computed column
        // (error 10609), even a persisted one — the filter can only reference IsDeleted. The index
        // itself still covers IsProfileComplete for the "who's incomplete" query.
        builder.HasIndex(c => new { c.AgencyId, c.IsProfileComplete })
            .HasFilter("[IsDeleted] = 0");
    }
}
