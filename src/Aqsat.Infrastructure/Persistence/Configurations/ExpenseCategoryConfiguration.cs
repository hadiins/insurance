using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ExpenseCategoryConfiguration : AqsatEntityConfiguration<ExpenseCategory>
{
    public override void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        base.Configure(builder);

        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
    }
}
