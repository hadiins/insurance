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

        // Owner decision 2026-09-21 (foreign nationals) — Kind distinguishes Iranian / 996-series
        // resident / passport-only; the passport is plaintext like the national ID (rule 12's
        // "shown in full" applies to any identifier the operator typed in) with the same keyed
        // HMAC alongside it for dedupe and future cross-agency paths.
        builder.Property(c => c.Kind).HasConversion<byte>().IsRequired();
        builder.Property(c => c.Kind).HasDefaultValue(Aqsat.Domain.Enums.CustomerKind.Iranian);
        builder.Property(c => c.PassportNumber).HasMaxLength(20);
        builder.Property(c => c.PassportExpiry).HasColumnType("date");

        // docs/TASK-25-IDENTITY-VEHICLE.md §3 — persisted so it can be indexed; SQL Server computes
        // it, the app never writes it (IsProfileComplete has a private setter for exactly this).
        builder.Property(c => c.IsProfileComplete)
            .HasComputedColumnSql(
                // Owner decision 2026-09-21 — a passport is a first-class identity: a customer
                // whose identifier is a passport is COMPLETE once the other fields exist, not
                // "incomplete forever" (COALESCE keeps Iranian rows' meaning byte-identical).
                "CASE WHEN COALESCE([NationalId], [PassportNumber]) IS NOT NULL AND [Mobile] IS NOT NULL " +
                "AND [Address] IS NOT NULL AND [PostalCode] IS NOT NULL " +
                "AND [FirstName] IS NOT NULL AND [LastName] IS NOT NULL " +
                "THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true);

        builder.HasIndex(c => new { c.AgencyId, c.ExternalCode }).IsUnique();
        builder.HasIndex(c => new { c.AgencyId, c.NationalIdHash });
        // Passport dedupe/lookup leads with AgencyId like every other index (rule 3).
        builder.HasIndex(c => new { c.AgencyId, c.PassportNumberHash });
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
