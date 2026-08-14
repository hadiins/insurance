using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ContractTemplateConfiguration : AqsatEntityConfiguration<ContractTemplate>
{
    public override void Configure(EntityTypeBuilder<ContractTemplate> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.ContractNamePattern).HasMaxLength(200).IsRequired();
        builder.Property(t => t.SuggestedDownPaymentPercent).HasPrecision(5, 2);
    }
}
