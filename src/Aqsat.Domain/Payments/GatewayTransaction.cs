using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;

namespace Aqsat.Domain;

/// <summary>
/// One attempt to collect money through a real payment gateway, from the moment the PSP accepts the
/// payment request until the server-side verify confirms it. It exists because a PSP redirects the
/// customer's BROWSER back to us — anonymously, carrying only the gateway's own reference — and that
/// callback has to be resolved to a business action with a known amount before anything may be
/// finalised. Without this row the callback would have to trust whatever amount the (untrusted)
/// request happened to contain.
///
/// Deliberately NOT implementing IAuditableEntity: like Payment, this row cannot resolve PolicyId
/// by itself for every purpose (an inquiry-fee transaction has no policy at all), and the money
/// movement already writes its own AuditEntry (AuditAction.PaymentRecorded) in the same transaction
/// as the Payment it produces. Claiming the generic audit path as well would double-log one event.
///
/// A row is created Pending when the PSP accepts the request and only ever reaches Verified after
/// VerifyAsync confirmed the money moved — never on the callback's own success flag, which is
/// attacker-controllable (CLAUDE.md: the database must refuse, not the caller be trusted).
/// </summary>
public class GatewayTransaction : AgencyOwnedEntity
{
    public PaymentProvider Provider { get; set; }

    public GatewayPurpose Purpose { get; set; }

    /// <summary>The gateway's own reference for this payment — GooyaPay's 32-char Authority,
    /// ZarinPal's Authority. What the callback looks the row up by. Uniqueness per provider is a
    /// unique index (the same string could in principle be issued by two different PSPs).</summary>
    public string GatewayReference { get; set; } = default!;

    /// <summary>The merchant credential the request was made with. Verify MUST be called with the
    /// same one — both PSPs reject a verify whose merchant does not own the transaction, and for
    /// the agency flows two different agencies share one platform deployment.</summary>
    public string MerchantIdUsed { get; set; } = default!;

    /// <summary>Toman (rule 19). Re-sent to the PSP at verify time and required to match, which is
    /// why the amount has to be read back from this row rather than from the callback.</summary>
    public decimal AmountToman { get; set; }

    public GatewayTransactionStatus Status { get; set; } = GatewayTransactionStatus.Pending;

    // ---- What to finalise once verify succeeds. Exactly one of the flow ids is the subject; the
    // rest are context kept for the audit trail and support ("which policy was this?"). ----

    /// <summary>Set for <see cref="GatewayPurpose.InquiryFee"/> and
    /// <see cref="GatewayPurpose.DownPayment"/> — the invitation whose stage advances.</summary>
    public Guid? InvitationId { get; set; }
    public CustomerPortalInvitation? Invitation { get; set; }

    /// <summary>Set for <see cref="GatewayPurpose.Installment"/> — the installment being settled.
    /// The owning customer comes from its policy.</summary>
    public Guid? InstallmentId { get; set; }
    public Installment? Installment { get; set; }

    /// <summary>Context only. Null for an inquiry-fee transaction taken out of a standalone
    /// (customer-file) invitation, which has no policy yet.</summary>
    public Guid? PolicyId { get; set; }
    public Policy? Policy { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // ---- Evidence returned by the PSP's verify call. ----

    /// <summary>The PSP's transaction id (GooyaPay RefID, ZarinPal ref_id) — the number a customer
    /// or an operator quotes when reconciling against the PSP panel.</summary>
    public string? RefId { get; set; }

    /// <summary>The masked card ("502229******5995") the payment was made with, as reported by
    /// verify. Transaction data, not a stored card number.</summary>
    public string? PaidCardMask { get; set; }

    /// <summary>The buyer's IP as reported by the PSP — kept for support, and comparable against
    /// our own request IP when investigating a disputed payment.</summary>
    public string? BuyerIp { get; set; }

    /// <summary>Persian reason, set only when the PSP reported a definite failure. Never contains
    /// anything about a person beyond what the PSP itself returned (rule 8).</summary>
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>When verify confirmed the payment. Null until then.</summary>
    public DateTimeOffset? VerifiedAtUtc { get; set; }

    /// <summary>After this, a callback for this reference is refused as abandoned rather than
    /// resurrecting a charge the customer has long stopped trying to make.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
