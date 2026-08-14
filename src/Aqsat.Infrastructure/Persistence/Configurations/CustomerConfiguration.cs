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

        // Encrypted at rest (CLAUDE.md rule 12) — plaintext only ever exists in memory.
        builder.Property(c => c.NationalId)
            .HasConversion(new ValueConverter<string?, byte[]?>(
                v => v == null ? null : encryptor.Encrypt(v),
                v => v == null ? null : encryptor.Decrypt(v)));

        builder.HasIndex(c => new { c.AgencyId, c.ExternalCode }).IsUnique();
        builder.HasIndex(c => new { c.AgencyId, c.NationalIdHash });
    }
}
