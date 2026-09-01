using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// Singleton row (at most one) holding the platform-level payment-gateway configuration — the
/// gateway the CUSTOMER's inquiry fee (کارمزد استعلام) is collected through, deposited to the
/// project owner's account. Platform-level exactly like ApiIrSettings/UpdatePackage: no AgencyId,
/// not RLS-scoped, Platform.Owner-only panel. Each AGENCY's own gateway (the customer's down
/// payment goes to the agency's account, not the owner's) lives on OrgSettings instead — two
/// different bank accounts, two different configs, one shared IPaymentGateway abstraction.
/// Provider is an enum, not a hard-coded vendor: ZarinPal today, any other PSP later.
/// </summary>
public class PlatformPaymentSettings : SoftDeletableEntity
{
    /// <summary>Mock = simulated gateway (no real money moves) — the safe default so a fresh
    /// install completes the whole portal flow before any credential exists. Same philosophy as
    /// ApiIrSettings.AllowPaidEndpoints defaulting to sandbox.</summary>
    public PaymentProvider Provider { get; set; } = PaymentProvider.Mock;

    /// <summary>The owner's merchant credential at the PSP (e.g. ZarinPal merchant id). Empty =
    /// real-gateway calls fail closed. Never returned in clear by the panel — masked like
    /// ApiIrSettings.ApiKey, so a screenshot of the panel can never leak a billable credential.</summary>
    public string OwnerMerchantId { get; set; } = string.Empty;

    /// <summary>The public base URL gateway callbacks are built from (e.g. https://api.aqsat.ir).
    /// One API deployment serves every agency, so one callback base covers both the owner-side
    /// (inquiry fee) and agent-side (down payment) flows. Null = fall back to configuration.</summary>
    public string? CallbackBaseUrl { get; set; }

    /// <summary>Global kill switch for money collection through the owner's gateway — when false,
    /// portal payment endpoints refuse regardless of any agency's own settings.</summary>
    public bool Enabled { get; set; }

    /// <summary>The inquiry fee each customer pays in the portal before the api.ir inquiries run
    /// (تومان) — an owner-account concern by definition, since the fee is collected through the
    /// owner's gateway into the owner's account (owner decision 2026-09-01). Snapshot-copied into
    /// each CustomerPortalInvitation at creation so later fee edits never retroactively change an
    /// invitation the customer may already be mid-flow on.</summary>
    public decimal InquiryFeeToman { get; set; } = 25000;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}