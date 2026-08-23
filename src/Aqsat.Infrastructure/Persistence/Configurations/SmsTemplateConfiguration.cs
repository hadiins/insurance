using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class SmsTemplateConfiguration : AqsatEntityConfiguration<SmsTemplate>
{
    public override void Configure(EntityTypeBuilder<SmsTemplate> builder)
    {
        base.Configure(builder);

        builder.Property(t => t.Key).HasMaxLength(60).IsRequired();
        builder.Property(t => t.Body).HasMaxLength(500).IsRequired();

        builder.HasIndex(t => new { t.AgencyId, t.Key })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
