using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class ReminderLogConfiguration : AqsatEntityConfiguration<ReminderLog>
{
    public override void Configure(EntityTypeBuilder<ReminderLog> builder)
    {
        base.Configure(builder);

        builder.Property(r => r.Mobile).HasMaxLength(15).IsRequired();
        builder.Property(r => r.TemplateKey).HasMaxLength(60).IsRequired();
        builder.Property(r => r.ProviderMessageId).HasMaxLength(100);

        builder.HasOne(r => r.Installment).WithMany().HasForeignKey(r => r.InstallmentId);
        builder.HasOne(r => r.RenewalWatch).WithMany().HasForeignKey(r => r.RenewalWatchId);

        // The idempotency key rule 25 depends on: "if already sent (installmentId, offset): skip".
        builder.HasIndex(r => new { r.AgencyId, r.InstallmentId, r.OffsetDays, r.RecipientType })
            .IsUnique()
            .HasFilter("[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(r => new { r.AgencyId, r.RenewalWatchId, r.OffsetDays, r.RecipientType })
            .IsUnique()
            .HasFilter("[RenewalWatchId] IS NOT NULL AND [IsDeleted] = 0");
    }
}
