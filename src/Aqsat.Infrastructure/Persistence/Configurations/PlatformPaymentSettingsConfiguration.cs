using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>
/// Platform-level payment-gateway settings — singleton like ApiIrSettings: the Platform.Owner
/// panel's GET/PUT is the only writer and reads-before-write, so two rows cannot arise from
/// ordinary use. No unique index (no natural key) and no AgencyId (not RLS-scoped).
/// </summary>
public sealed class PlatformPaymentSettingsConfiguration : AqsatEntityConfiguration<PlatformPaymentSettings>
{
    public override void Configure(EntityTypeBuilder<PlatformPaymentSettings> builder)
    {
        base.Configure(builder);

        builder.Property(s => s.OwnerMerchantId).HasMaxLength(128);
        builder.Property(s => s.CallbackBaseUrl).HasMaxLength(300);

        // Store the enum by its string ("Mock"/"ZarinPal"), matching how the API layer speaks
        // enums everywhere — a raw int in the DB would make every manual inspection ambiguous.
        builder.Property(s => s.Provider)
            .HasConversion<string>()
            .HasMaxLength(32);
    }
}