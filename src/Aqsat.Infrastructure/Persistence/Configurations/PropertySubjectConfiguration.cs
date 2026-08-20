using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class PropertySubjectConfiguration : AqsatEntityConfiguration<PropertySubject>
{
    public override void Configure(EntityTypeBuilder<PropertySubject> builder)
    {
        base.Configure(builder);

        builder.Property(p => p.Address).HasMaxLength(400).IsRequired();
        builder.Property(p => p.PostalCode).HasMaxLength(10);
        builder.Property(p => p.Type).HasMaxLength(60);
        builder.Property(p => p.Value).HasPrecision(18, 0);
    }
}
