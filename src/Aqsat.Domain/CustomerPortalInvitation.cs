using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// A TTL-limited link the agency texts a customer so they can pay the inquiry fee (کارمزد
/// استعلام) in the public portal, docs/CUSTOMER-PORTAL-SPEC.md §3. Token is 43-char
/// Base64Url from 32 CSPRNG bytes — unguessable, looked up by (AgencyId, Token). The fee is
/// snapshot-copied from OrgSettings at creation so a later fee edit never changes a link the
/// customer may already be mid-flow on. Agency-owned (RLS-scoped); the public portal resolves
/// the agency through the RLS-exempt PortalInvitationTokenIndex (written in the same
/// transaction) before any RLS read.
/// </summary>
public class CustomerPortalInvitation : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    public string Token { get; set; } = default!;

    /// <summary>Snapshot of PlatformPaymentSettings.InquiryFeeToman at creation (toman) — the fee
    /// is an owner-account concern, collected through the owner's gateway.</summary>
    public decimal InquiryFeeToman { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public PortalInvitationStatus Status { get; set; } = PortalInvitationStatus.Pending;

    public DateTimeOffset? PaidAtUtc { get; set; }

    /// <summary>The amount actually settled (toman) — equals InquiryFeeToman in the mock
    /// gateway, may differ under a real PSP (partial capture, gateway fees).</summary>
    public decimal? PaidAmountToman { get; set; }
}
