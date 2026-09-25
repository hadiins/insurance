using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

/// <summary>
/// The pending/completed gateway charge behind every real-PSP payment. Every rule that matters for
/// a table money moves through is spelled out here rather than left to convention:
/// FK + Restrict on every relationship (rule 4/6), an index for each FK (rule 5), AgencyId leading
/// every index (rule 3), toman precision with no fractions (rule 19), soft delete with RowVersion.
/// The migration additionally joins AgencyAccessPolicy — RLS is applied in raw SQL, not here (rule 10).
/// </summary>
public sealed class GatewayTransactionConfiguration : AqsatEntityConfiguration<GatewayTransaction>
{
    public override void Configure(EntityTypeBuilder<GatewayTransaction> builder)
    {
        base.Configure(builder);

        // Stored as its string name, matching OrgSettings.PaymentProvider and
        // PlatformPaymentSettings.Provider — a support engineer reading this table must not have to
        // map "3" back to a gateway in their head while chasing a missing payment.
        builder.Property(t => t.Provider)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.GatewayReference).HasMaxLength(128).IsRequired();
        builder.Property(t => t.MerchantIdUsed).HasMaxLength(128).IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(500);
        builder.Property(t => t.RefId).HasMaxLength(64);
        builder.Property(t => t.PaidCardMask).HasMaxLength(32);
        builder.Property(t => t.BuyerIp).HasMaxLength(64);

        // Toman has no fractions — same precision as Payment.Amount, Installment.Amount and
        // OrgSettings.MonthlyCollectionGoal.
        builder.Property(t => t.AmountToman).HasPrecision(18, 0);

        // The callback's exact lookup path: the agency is resolved from our own token first, the
        // session is stamped, and the row is then found inside that agency's RLS scope — so AgencyId
        // leads (rule 3), and uniqueness is per (agency, provider, reference), which is all the
        // uniqueness the flow actually needs: two agencies can never be confused for each other
        // because the token already fixed which agency we are looking in.
        builder.HasIndex(t => new { t.AgencyId, t.Provider, t.GatewayReference })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // Abandoned-charge expiry and the operator's own reconciliation view.
        builder.HasIndex(t => new { t.AgencyId, t.Status, t.CreatedAtUtc });

        // Rule 5 — SQL Server does not create a FK index automatically. Each one leads with AgencyId.
        builder.HasIndex(t => new { t.AgencyId, t.InvitationId });
        builder.HasIndex(t => new { t.AgencyId, t.InstallmentId });
        builder.HasIndex(t => new { t.AgencyId, t.PolicyId });
        builder.HasIndex(t => new { t.AgencyId, t.CustomerId });

        // Rule 4 — real FOREIGN KEYs in the database, Restrict (rule 6), never Cascade.
        builder.HasOne(t => t.Invitation)
            .WithMany()
            .HasForeignKey(t => t.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Installment)
            .WithMany()
            .HasForeignKey(t => t.InstallmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Policy)
            .WithMany()
            .HasForeignKey(t => t.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Customer)
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
