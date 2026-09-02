using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class BankConfiguration : AqsatEntityConfiguration<Bank>
{
    public override void Configure(EntityTypeBuilder<Bank> builder)
    {
        base.Configure(builder);

        builder.Property(b => b.Name).HasMaxLength(80).IsRequired();

        builder.HasIndex(b => new { b.AgencyId, b.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
