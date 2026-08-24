using Aqsat.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class BankAccountConfiguration : AqsatEntityConfiguration<BankAccount>
{
    public override void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        base.Configure(builder);

        builder.Property(b => b.BankName).HasMaxLength(80).IsRequired();
        builder.Property(b => b.AccountNumber).HasMaxLength(40).IsRequired();
        builder.Property(b => b.AccountHolderName).HasMaxLength(120);
    }
}
