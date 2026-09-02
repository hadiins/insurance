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
///
/// When PolicyId is set, the same link also carries the staged issuance-verification chain
/// (Stage): fee payment → credit inquiries → agency decision → customer contract approval →
/// down payment, all between wizard steps 3 and 4 (owner decision 2026-09-01).
/// </summary>
public class CustomerPortalInvitation : AgencyOwnedEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!;

    /// <summary>The installment policy this link verifies. Null = the plain inquiry-fee link the
    /// customer file issues, which never walks the Stage chain.</summary>
    public Guid? PolicyId { get; set; }
    public Policy? Policy { get; set; }

    public string Token { get; set; } = default!;

    /// <summary>Snapshot of PlatformPaymentSettings.InquiryFeeToman at creation (toman) — the fee
    /// is an owner-account concern, collected through the owner's gateway.</summary>
    public decimal InquiryFeeToman { get; set; }

    /// <summary>Snapshot of the policy's down payment (toman) at creation — the amount the portal
    /// charges in the chain's final step. Null when PolicyId is null.</summary>
    public decimal? DownPaymentAmountToman { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public PortalInvitationStatus Status { get; set; } = PortalInvitationStatus.Pending;

    public DateTimeOffset? PaidAtUtc { get; set; }

    /// <summary>The amount actually settled (toman) — equals InquiryFeeToman in the mock
    /// gateway, may differ under a real PSP (partial capture, gateway fees).</summary>
    public decimal? PaidAmountToman { get; set; }

    /// <summary>Where this link sits in the issuance-verification chain — only meaningful when
    /// PolicyId is set.</summary>
    public PolicyVerificationStage Stage { get; set; } = PolicyVerificationStage.FeePending;

    public DateTimeOffset? AgencyDecisionAtUtc { get; set; }
    public Guid? AgencyDecisionByUserId { get; set; }
    public DateTimeOffset? CustomerApprovedAtUtc { get; set; }
    public DateTimeOffset? DownPaymentPaidAtUtc { get; set; }
}
