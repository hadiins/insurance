using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>1:1 extension of Organization keyed directly on OrganizationId — not the Entity Id/BizId pattern.</summary>
public sealed class OrgSettingsConfiguration : IEntityTypeConfiguration<OrgSettings>
{
    public void Configure(EntityTypeBuilder<OrgSettings> builder)
    {
        builder.HasKey(s => s.OrganizationId);

        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Property(s => s.ReminderDaysBefore).HasMaxLength(40).IsRequired();
        builder.Property(s => s.AgentMerchantId).HasMaxLength(128);
        builder.Property(s => s.SmsApiKey).HasMaxLength(256);
        builder.Property(s => s.DangerZoneManagerMobile).HasMaxLength(20);
        builder.Property(s => s.PaymentProvider)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne(s => s.Organization)
            .WithOne()
            .HasForeignKey<OrgSettings>(s => s.OrganizationId);
    }
}
