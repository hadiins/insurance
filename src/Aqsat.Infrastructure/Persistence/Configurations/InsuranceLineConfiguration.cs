using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class InsuranceLineConfiguration : AqsatEntityConfiguration<InsuranceLine>
{
    public override void Configure(EntityTypeBuilder<InsuranceLine> builder)
    {
        base.Configure(builder);

        builder.Property(l => l.Code).HasMaxLength(30).IsRequired();
        builder.Property(l => l.NameFa).HasMaxLength(80).IsRequired();

        builder.HasOne(l => l.Parent).WithMany().HasForeignKey(l => l.ParentId);

        builder.HasIndex(l => l.Code).IsUnique();
    }
}
